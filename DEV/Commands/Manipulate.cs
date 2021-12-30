using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace DEV {
  public class ManipulateCommand : BaseCommands {
    private static bool IsIncluded(string id, string name) {
      if (id.StartsWith("*") && id.EndsWith("*")) {
        return name.Contains(id.Substring(1, id.Length - 3));
      }
      if (id.StartsWith("*")) return name.EndsWith(id.Substring(1));
      if (id.EndsWith("*")) return name.StartsWith(id.Substring(0, id.Length - 2));
      return id == name;
    }
    public static IEnumerable<int> GetPrefabs(string id) {
      IEnumerable<GameObject> values = ZNetScene.instance.m_namedPrefabs.Values;
      if (id == "*") { } // Pass
      else if (id.Contains("*"))
        values = values.Where(prefab => IsIncluded(id, prefab.name));
      else
        values = values.Where(prefab => prefab.name == id);
      return values.Where(prefab => prefab.name != "Player").Select(prefab => prefab.name.GetStableHashCode());
    }
    public static IEnumerable<ZDO> GetZDOs(string id, float distance) {
      var codes = GetPrefabs(id);
      var sector = Player.m_localPlayer ? Player.m_localPlayer.m_nview.GetZDO().GetSector() : new Vector2i(0, 0);
      var index = ZDOMan.instance.SectorToIndex(sector);
      IEnumerable<ZDO> zdos = ZDOMan.instance.m_objectsBySector[index];
      zdos = zdos.Where(zdo => codes.Contains(zdo.GetPrefab()));
      var position = Player.m_localPlayer ? Player.m_localPlayer.transform.position : Vector3.zero;
      if (distance > 0)
        return zdos.Where(zdo => Utils.DistanceXZ(zdo.GetPosition(), position) <= distance);
      return zdos;
    }
    public ManipulateCommand() {
      new Terminal.ConsoleCommand("move", "[x,z,y] [player/object/world] - Moves hovered object relative to selected origin (relative to player if not given).", delegate (Terminal.ConsoleEventArgs args) {
        var view = GetHovered(args);
        if (!view) return;
        var zdo = view.GetZDO();
        var position = view.transform.position;
        var relative = ParsePositionXZY(args.Args[1]);
        var origin = args.Length > 2 ? args[2].ToLower() : "player";
        var rotation = Player.m_localPlayer.transform.rotation;
        if (origin == "world")
          rotation = Quaternion.identity;
        if (origin == "object")
          rotation = view.transform.rotation;
        position += rotation * Vector3.forward * relative.x;
        position += rotation * Vector3.right * relative.z;
        position += rotation * Vector3.up * relative.y;
        zdo.SetPosition(position);
        view.transform.position = position;
      }, true, false, true, false, false);
      new Terminal.ConsoleCommand("rotate", "[x,y,z/reset] [player/object/world] - Rotates hovered object around a given axis relative to selected origin (relative to player y axis if not given).", delegate (Terminal.ConsoleEventArgs args) {
        var view = GetHovered(args);
        if (!view) return;
        var zdo = view.GetZDO();
        if (args[1].ToLower() == "reset") {
          zdo.SetRotation(Quaternion.identity);
          view.transform.rotation = Quaternion.identity;
          return;
        }
        var relative = ParsePositionYXZ(args.Args[1]);
        var origin = args.Length > 2 ? args[2].ToLower() : "player";
        var originRotation = Player.m_localPlayer.transform.rotation;
        if (origin == "world")
          originRotation = Quaternion.identity;
        if (origin == "object")
          originRotation = view.transform.rotation;
        var transform = view.transform;
        var position = transform.position;
        transform.RotateAround(position, originRotation * Vector3.up, relative.y);
        transform.RotateAround(position, originRotation * Vector3.forward, relative.x);
        transform.RotateAround(position, originRotation * Vector3.right, relative.z);
        zdo.SetRotation(view.transform.rotation);
      }, true, false, true, false, false);
      new Terminal.ConsoleCommand("scale", "[x,z,y] - Scales hovered object.", delegate (Terminal.ConsoleEventArgs args) {
        var view = GetHovered(args);
        if (!view) return;
        var scale = ParsePositionXZY(args.Args[1], Vector3.one);
        if (view.m_syncInitialScale)
          view.SetLocalScale(scale);
      }, true, false, true, false, false);
      new Terminal.ConsoleCommand("target", "[operation] [amount] [id=*] [radius=0] - Modifies the targeted object.", delegate (Terminal.ConsoleEventArgs args) {
        if (args.Length < 2) return;
        var operation = args[1];
        if (!Operations.Contains(operation)) {
          AddMessage(args.Context, "Error: Invalid operation.");
          return;
        }
        var radiusArg = args.Args.FirstOrDefault(arg => arg.StartsWith("radius="));
        var idArg = args.Args.FirstOrDefault(arg => arg.StartsWith("id="));
        var id = idArg == null ? "*" : TryParameterString(idArg.Split('='), 1, "*");
        IEnumerable<ZDO> zdos;
        if (radiusArg != null) {
          var radius = TryParameterFloat(radiusArg.Split('='), 1, 100f);
          radius = Math.Min(radius, 100f);
          zdos = GetZDOs(id, radius);
        } else {
          var view = GetHovered(args);
          if (!view) return;
          if (!GetPrefabs(id).Contains(view.GetZDO().GetPrefab())) {
            AddMessage(args.Context, "Skipped: ¤ has invalid id.");
            return;
          }
          zdos = new ZDO[] { view.GetZDO() };
        }
        var scene = ZNetScene.instance;
        foreach (var zdo in zdos) {
          var view = scene.FindInstance(zdo);
          if (view == null) {
            args.Context.AddString("Skipped: ¤ is not loaded.");
            continue;
          }
          var character = view.GetComponent<Character>();
          var output = "Error: Invalid operation.";
          if (operation == "health")
            output = ChangeHealth(character, args.TryParameterInt(2, 1));
          if (operation == "stars")
            output = SetStars(character, args.TryParameterInt(2, 1));
          if (operation == "tame")
            output = MakeTame(character);
          if (operation == "wild")
            output = MakeWild(character);
          if (operation == "baby")
            output = SetBaby(view.GetComponent<Growup>());
          if (operation == "info")
            output = GetInfo(view);
          if (operation == "sleep")
            output = MakeSleep(view.GetComponent<MonsterAI>());
          if (operation == "remove") {
            Actions.Remove(view.gameObject);
            output = "Entity ¤ destroyed.";
          }
          var message = output.Replace("¤", Utils.GetPrefabName(view.gameObject));
          if (radiusArg == null)
            AddMessage(args.Context, message);
          else
            args.Context.AddString(message);
        }
      }, true, false, true, false, false, () => Operations);
    }
    private static List<string> Operations = new List<string>(){
      "health",
      "stars",
      "baby",
      "wild",
      "tame",
      "info",
      "sleep",
      "remove"
    };
    private static string ChangeHealth(Character obj, int amount) {
      if (obj == null) return "Skipped: ¤ is not a creature..";
      var previous = obj.GetMaxHealth();
      Actions.SetHealth(obj, amount);
      return $"¤ health changed from {previous.ToString("F0")} to {amount.ToString("F0")}.";
    }
    private static string SetStars(Character obj, int amount) {
      if (obj == null) return "Skipped: ¤ is not a creature.";
      var previous = obj.GetLevel() - 1;
      Actions.SetLevel(obj.gameObject, amount + 1);
      return $"¤ stars changed from {previous} to {amount}.";
    }
    private static string SetBaby(Growup obj) {
      if (obj == null) return "Skipped: ¤ is not an offspring.";
      Actions.SetBaby(obj);
      return "¤ growth disabled.";
    }
    private static string MakeTame(Character obj) {
      if (obj == null) return "Skipped: ¤ is not a creature.";
      Actions.SetTame(obj, true);
      return "¤ made tame.";
    }
    private static string MakeWild(Character obj) {
      if (obj == null) return "Skipped: ¤ is not a creature.";
      Actions.SetTame(obj, false);
      return "¤ made wild.";
    }
    private static string MakeSleep(MonsterAI obj) {
      if (obj == null) return "Skipped: ¤ is not a creature.";
      Actions.SetSleeping(obj, true);
      return "¤ made to sleep.";
    }
    private static string GetInfo(ZNetView obj) {
      var info = new List<string>();
      info.Add("Id: ¤");
      info.Add("Pos: " + obj.transform.position.ToString("F1"));
      var character = obj.GetComponent<Character>();
      if (character) {
        info.Add("Health: " + character.GetHealth().ToString("F0") + " / " + character.GetMaxHealth());
        info.Add("Stars: " + (character.GetLevel() - 1));
        info.Add("Tamed: " + (character.IsTamed() ? "Yes" : "No"));
        var growUp = obj.GetComponent<Growup>();
        if (growUp)
          info.Add("Baby: " + (growUp.m_baseAI.GetTimeSinceSpawned().TotalSeconds < 0 ? "Yes" : "No"));
      }
      return string.Join(", ", info);
    }
  }
}
