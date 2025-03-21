using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FFXIVClientStructs.FFXIV.Client.System.Framework;

namespace ARealmRecordedLite.Managers;

public unsafe partial class ReplayFileManager
{
    public static List<(FileInfo, FFXIVReplay)> ReplayList
    {
        get => replayList ?? GetReplayList();
        set => replayList = value;
    }

    public static string?            LastSelectedReplay { get; set; }
    public static FFXIVReplay.Header LastSelectedHeader { get; set; }

    public static string ReplayFolder      => Path.Join(Framework.Instance()->UserPathString, "replay");
    public static string AutoRenamedFolder => Path.Join(ReplayFolder,                         "autorenamed");
    public static string DeletedFolder     => Path.Join(ReplayFolder,                         "deleted");
    public static string ArchiveZip        => Path.Join(ReplayFolder,                         "archive.zip");
    
    private static List<(FileInfo, FFXIVReplay)>? replayList;
    private static bool                           WasRecording;
    
    internal static void Init()
    {
        ContentsReplayModule.SetSavedReplayCIDs();

        if (ContentsReplayModule.Instance()->InPlayback && ContentsReplayModule.Instance()->FileStream != nint.Zero &&
            *(long*)ContentsReplayModule.Instance()->FileStream                                        == 0)
            ReplayManager.LoadReplay(Service.Config.LastLoadedReplay);
    }

    internal static void Uninit()
    {
        if (ContentsReplayModule.Instance() == null) return;
        ContentsReplayModule.SetSavedReplayCIDs();
    }

    public static string GetReplaySlotName(int slot) => $"FFXIV_{Service.ClientState.LocalContentId:X16}_{slot:D3}.dat";

    public static void UpdateAutoRename()
    {
        switch (ContentsReplayModule.Instance()->IsRecording)
        {
            case true when !WasRecording:
                WasRecording = true;
                break;
            case false when WasRecording:
                WasRecording = false;
                Service.Framework.RunOnTick(() =>
                {
                    AutoRenameReplays();
                    ContentsReplayModule.SetSavedReplayCIDs();
                }, TimeSpan.Zero, 30);
                break;
        }
    }

    public static FFXIVReplay* ReadReplay(string path)
    {
        var ptr       = nint.Zero;
        var allocated = false;

        try
        {
            using var fs = File.OpenRead(path);

            ptr       = Marshal.AllocHGlobal((int)fs.Length);
            allocated = true;

            _ = fs.Read(new Span<byte>((void*)ptr, (int)fs.Length));
        }
        catch (Exception e)
        {
            Service.Log.Error(e, $"无法读取回放: {path}");

            if (allocated)
            {
                Marshal.FreeHGlobal(ptr);
                ptr = nint.Zero;
            }
        }

        return (FFXIVReplay*)ptr;
    }

    public static FFXIVReplay? ReadReplayHeaderAndChapters(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            
            var size  = sizeof(FFXIVReplay.Header) + sizeof(FFXIVReplay.ChapterArray);
            var bytes = new byte[size];
            if (fs.Read(bytes, 0, size) != size)
                return null;

            fixed (byte* ptr = bytes)
                return *(FFXIVReplay*)ptr;
        }
        catch (Exception e)
        {
            Service.Log.Error(e, $"无法读取回放 {path}");
            return null;
        }
    }

    public static List<(FileInfo, FFXIVReplay)> GetReplayList()
    {
        try
        {
            var directory = new DirectoryInfo(ReplayFolder);

            var renamedDirectory = new DirectoryInfo(AutoRenamedFolder);
            if (!renamedDirectory.Exists)
            {
                if (Service.Config.MaxAutoRenamedReplays > 0)
                    renamedDirectory.Create();
                else
                    renamedDirectory = null;
            }

            var list = (from file in directory.GetFiles().Concat(renamedDirectory?.GetFiles() ?? [])
                        where file.Extension == ".dat"
                        let replay = ReadReplayHeaderAndChapters(file.FullName)
                        where replay is { ReplayHeader.IsValid: true }
                        select (file, replay.Value)
                       ).ToList();

            replayList = list;
        }
        catch { replayList = []; }

        return replayList;
    }

    public static void RenameReplay(FileInfo file, string name)
    {
        try
        {
            file.MoveTo(Path.Combine(ReplayFolder, $"{name}.dat"));
        }
        catch (Exception e)
        {
            Service.Log.Error(e, "重命名录像失败");
        }
    }

    public static void AutoRenameReplays()
    {
        if (Service.Config.MaxAutoRenamedReplays <= 0)
        {
            GetReplayList();
            return;
        }

        try
        {
            var (file, replay) = GetReplayList().Where(t => t.Item1.Name.StartsWith("FFXIV_")).MaxBy(t => t.Item1.LastWriteTime);

            var name =
                $"{BannedFileCharactersRegex().Replace(ContentsReplayModule.Instance()->ContentTitle.ToString(), string.Empty)} {DateTime.Now:yyyy.MM.dd HH.mm.ss}";
            file.MoveTo(Path.Combine(AutoRenamedFolder, $"{name}.dat"));

            var renamedFiles = new DirectoryInfo(AutoRenamedFolder).GetFiles().Where(f => f.Extension == ".dat").ToList();
            while (renamedFiles.Count > Service.Config.MaxAutoRenamedReplays)
            {
                DeleteReplay(renamedFiles.OrderBy(f => f.CreationTime).First());
                renamedFiles = new DirectoryInfo(AutoRenamedFolder).GetFiles().Where(f => f.Extension == ".dat").ToList();
            }

            GetReplayList();

            for (var i = 0; i < 3; i++)
            {
                if (ContentsReplayModule.Instance()->SavedReplayHeaders[i].Timestamp != replay.ReplayHeader.Timestamp) continue;
                ContentsReplayModule.Instance()->SavedReplayHeaders[i] = new FFXIVReplay.Header();
                break;
            }
        }
        catch (Exception e)
        {
            Service.Log.Error(e, "重命名录像失败");
        }
    }

    public static void DeleteReplay(FileInfo file)
    {
        try
        {
            if (Service.Config.MaxDeletedReplays > 0)
            {
                var deletedDirectory = new DirectoryInfo(DeletedFolder);
                if (!deletedDirectory.Exists)
                    deletedDirectory.Create();

                file.MoveTo(Path.Combine(DeletedFolder, file.Name), true);

                var deletedFiles = deletedDirectory.GetFiles().Where(f => f.Extension == ".dat").ToList();
                while (deletedFiles.Count > Service.Config.MaxDeletedReplays)
                {
                    deletedFiles.OrderBy(f => f.CreationTime).First().Delete();
                    deletedFiles = deletedDirectory.GetFiles().Where(f => f.Extension == ".dat").ToList();
                }
            }
            else
                file.Delete();


            GetReplayList();
        }
        catch (Exception e)
        {
            Service.Log.Error(e, "删除录像失败");
        }
    }

    public static void ArchiveReplays()
    {
        var archivableReplays = ReplayList.Where(t => !t.Item2.ReplayHeader.IsPlayable && t.Item1.Directory?.Name == "replay").ToArray();
        if (archivableReplays.Length == 0) return;

        var restoreBackup = true;

        try
        {
            using (var zipFileStream = new FileStream(ArchiveZip, FileMode.OpenOrCreate))
            using (var zipFile = new ZipArchive(zipFileStream, ZipArchiveMode.Update))
            {
                var expectedEntryCount = zipFile.Entries.Count;
                if (expectedEntryCount > 0)
                {
                    var prevPosition = zipFileStream.Position;
                    zipFileStream.Position = 0;
                    using var zipBackupFileStream = new FileStream($"{ArchiveZip}.BACKUP", FileMode.Create);
                    zipFileStream.CopyTo(zipBackupFileStream);
                    zipFileStream.Position = prevPosition;
                }

                foreach (var (file, _) in archivableReplays)
                {
                    zipFile.CreateEntryFromFile(file.FullName, file.Name);
                    expectedEntryCount++;
                }

                if (zipFile.Entries.Count != expectedEntryCount)
                    throw new IOException(
                        $"Number of archived replays was unexpected (Expected: {expectedEntryCount}, Actual: {zipFile.Entries.Count}) after archiving, restoring backup!");
            }

            restoreBackup = false;

            foreach (var (file, _) in archivableReplays)
                file.Delete();
        }
        catch (Exception e)
        {
            if (restoreBackup)
                try
                {
                    using var zipBackupFileStream = new FileStream($"{ArchiveZip}.BACKUP", FileMode.Open);
                    using var zipFileStream       = new FileStream(ArchiveZip,             FileMode.Create);
                    zipBackupFileStream.CopyTo(zipFileStream);
                }
                catch
                {
                    // ignored
                }

            Service.Log.Error(e, "尝试归档录像时发生错误");
        }

        GetReplayList();
    }

    public static void SetDutyRecorderMenuSelection(nint agent, byte slot)
    {
        *(byte*)(agent + 0x2C) = slot;
        *(byte*)(agent + 0x2A) = 1;
        ReplayManager.DisplaySelectedDutyRecording(agent);
    }

    public static void SetDutyRecorderMenuSelection(nint agent, string path, FFXIVReplay.Header header)
    {
        header.LocalCID    = Service.ClientState.LocalContentId;
        LastSelectedReplay = path;
        LastSelectedHeader = header;

        var prevHeader = ContentsReplayModule.Instance()->SavedReplayHeaders[0];
        ContentsReplayModule.Instance()->SavedReplayHeaders[0] = header;

        SetDutyRecorderMenuSelection(agent, 0);
        ContentsReplayModule.Instance()->SavedReplayHeaders[0] = prevHeader;

        *(byte*)(agent + 0x2C) = 100;
    }

    public static void CopyReplayIntoSlot(nint agent, FileInfo file, FFXIVReplay.Header header, byte slot)
    {
        if (slot > 2) return;

        try
        {
            file.CopyTo(Path.Combine(ReplayFolder, GetReplaySlotName(slot)), true);

            header.LocalCID = Service.ClientState.LocalContentId;

            ContentsReplayModule.Instance()->SavedReplayHeaders[slot] = header;
            SetDutyRecorderMenuSelection(agent, slot);
            GetReplayList();
        }
        catch (Exception e)
        {
            Service.Log.Error(e, $"将录像复制到第 {slot + 1} 槽时发生错误");
        }
    }

    public static void OpenReplayFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = ReplayFolder,
                UseShellExecute = true
            });
        }
        catch
        {
            // ignored
        }
    }
    
    
    [GeneratedRegex("[\\\\\\/:\\*\\?\"\\<\\>\\|\u0000-\u001F]")]
    private static partial Regex BannedFileCharactersRegex();
}
