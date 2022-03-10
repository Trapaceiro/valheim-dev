using System;
using System.Collections.Generic;
using System.Linq;

namespace ServerDevcommands {
  ///<summary>Feature for disabling commands.</summary>
  public class BlackList {

    public static List<string> AllowedCommands = new List<string>();
    public static List<string> DisallowedCommands = new List<string>();
    public static HashSet<string> RootUsers = new HashSet<string>();

    public static bool CanRun(string command, ZRpc rpc = null) {
      if (rpc != null && rpc.m_socket != null && RootUsers.Contains(rpc.m_socket.GetHostName())) return true;
      if (DisallowedCommands.Contains(command.ToLower())) return false;
      if (DisallowedCommands.Any(black => command.StartsWith((black + " "), StringComparison.OrdinalIgnoreCase))) return false;
      return true;
    }

    public static void UpdateCommands(string rootUsers, string blackList) {
      RootUsers = rootUsers.Split(',').Select(s => s.Trim()).ToHashSet();
      DisallowedCommands = blackList.Split(',').Select(s => s.Trim().ToLower()).ToList();
      AllowedCommands = Terminal.commands.Keys.Where(s => CanRun(s)).ToList();
    }
  }
}
