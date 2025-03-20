using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Dalamud.Game.Command;
using SamplePlugin.Windows;

namespace SamplePlugin.Managers;

public sealed class CommandManager
{
    public const string MainCommand = "/pdr";

    private static readonly ConcurrentDictionary<string, CommandInfo> addedCommands = [];
    private static readonly ConcurrentDictionary<string, CommandInfo> subCommands    = [];
    
    internal void Init()
    {
        RefreshCommandDetails();
        InternalCommands.Init();
    }

    private static void RefreshCommandDetails()
    {
        var helpMessage = new StringBuilder("打开主界面\n");
        
        foreach (var (command, commandInfo) in subCommands.Where(x => x.Value.ShowInHelp))
            helpMessage.AppendLine($"{MainCommand} {command} → {commandInfo.HelpMessage}");

        RemoveCommand(MainCommand);
        AddCommand(MainCommand, new CommandInfo(OnCommandPDR) { HelpMessage = helpMessage.ToString() }, true);
    }

    public static bool AddCommand(string command, CommandInfo commandInfo, bool isForceToAdd = false)
    {
        if (!isForceToAdd && DService.Command.Commands.ContainsKey(command)) return false;

        RemoveCommand(command);
        DService.Command.AddHandler(command, commandInfo);
        addedCommands[command] = commandInfo;

        return true;
    }

    public static bool RemoveCommand(string command)
    {
        if (DService.Command.Commands.ContainsKey(command))
        {
            DService.Command.RemoveHandler(command);
            addedCommands.TryRemove(command, out _);
            return true;
        }

        return false;
    }

    public static bool AddSubCommand(string args, CommandInfo commandInfo, bool isForceToAdd = false)
    {
        if (!isForceToAdd && subCommands.ContainsKey(args)) return false;

        subCommands[args] = commandInfo;
        RefreshCommandDetails();
        return true;
    }

    public static bool RemoveSubCommand(string args)
    {
        if (subCommands.TryRemove(args, out _))
        {
            RefreshCommandDetails();
            return true;
        }

        return false;
    }

    private static void OnCommandPDR(string command, string args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            if (WindowManager.Get<Main>() is { } main)
                main.IsOpen ^= true;
            return;
        }

        var spitedArgs = args.Split(' ', 2);
        if (subCommands.TryGetValue(spitedArgs[0], out var commandInfo))
            commandInfo.Handler(spitedArgs[0], spitedArgs.Length > 1 ? spitedArgs[1] : string.Empty);
        else
            ChatError($"子命令 {spitedArgs[0]} 不存在");
    }

    internal void Uninit()
    {
        foreach (var command in addedCommands.Keys)
            RemoveCommand(command);

        addedCommands.Clear();
        subCommands.Clear();
    }

    private static class InternalCommands
    {
        internal static void Init()
        {
            
        }
    }
}
