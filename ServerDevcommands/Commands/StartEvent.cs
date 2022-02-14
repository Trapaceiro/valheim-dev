using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ServerDevcommands {

  ///<summary>Adds coordinates to the parameters (required for server side support).</summary>
  public class StartEventCommand {
    private static void DoStartEvent(string[] args, Terminal context) {
      if (args.Length < 2) return;
      var name = args[1];
      if (!RandEventSystem.instance.HaveEvent(name)) {
        context.AddString("Random event not found:" + name);
        return;
      }
      var x = Parse.TryFloat(args, 2, 0);
      var z = Parse.TryFloat(args, 3, 0);
      RandEventSystem.instance.SetRandomEventByName(name, new Vector3(x, 0, z));
    }
    public StartEventCommand() {
      new Terminal.ConsoleCommand("event", "[name] [x] [z] - start event.", delegate (Terminal.ConsoleEventArgs args) {
        if (args.Length < 2) return;
        var parameters = Helper.AddPlayerPosXZ(args.Args, 2);
        if (ZNet.instance.IsServer()) DoStartEvent(parameters, args.Context);
        else ServerCommand.Send(parameters);
      }, true, true, optionsFetcher: () => RandEventSystem.instance.m_events.Select(ev => ev.m_name).ToList());
      AutoComplete.Register("event", (int index) => {
        if (index == 0) return Terminal.commands["event"].m_tabOptionsFetcher();
        if (index == 1) return ParameterInfo.Create("X coordinate", "number (default is the current position)");
        if (index == 2) return ParameterInfo.Create("Z coordinate", "number (default is the current position)");
        return null;
      });
    }
  }

  [HarmonyPatch(typeof(RandEventSystem), "StartRandomEvent")]
  public class DisableRandomEvents {
    public static bool Prefix() => !Settings.DisableEvents;
  }
}