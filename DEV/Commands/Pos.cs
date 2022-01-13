using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace DEV {

  public class PosCommand : BaseCommands {
    ///<summary>Adds support for other player's position. </summary>
    public PosCommand() {
      new Terminal.ConsoleCommand("pos", "[name] - Prints the position of a player. If name is not given, prints the current position.", delegate (Terminal.ConsoleEventArgs args) {
        var position = Player.m_localPlayer?.transform.position;
        if (args.Length >= 2) {
          var info = FindPlayer(args[1]);
          position = info.m_characterID.IsNone() ? null : (Vector3?)info.m_position;
        }
        if (position.HasValue)
          AddMessage(args.Context, "Player position (X,Y,Z):" + position.Value.ToString("F0"));
        else
          AddMessage(args.Context, "Error: Unable to find the player.");
      }, true, true);
    }
  }

  ///<summary>Server side code to include private player positions.</summary>
  [HarmonyPatch(typeof(ZNet), "UpdatePlayerList")]
  public class Server_UpdatePrivatePositions {
    public static void Postfix(ZNet __instance) {
      if (!__instance.IsServer() || !Settings.ShowPrivatePlayers) return;
      var idToPeer = __instance.m_peers.ToDictionary(peer => peer.m_characterID, peer => peer);
      for (var i = 0; i < __instance.m_players.Count; i++) {
        var player = __instance.m_players[i];
        if (player.m_characterID == __instance.m_characterID) continue;
        if (!idToPeer.TryGetValue(player.m_characterID, out var peer)) {
          DEV.Log.LogError("Unable to find the peer to set private position.");
          continue;
        }
        if (peer.m_publicRefPos) continue;
        player.m_position = peer.m_refPos;
        __instance.m_players[i] = player;
      }
    }
  }
  ///<summary>Server side code to send private players.</summary>
  [HarmonyPatch(typeof(ZNet), "SendPlayerList")]
  public class SendPrivatePositionsToAdmins {
    private static void SendToAdmins(ZNet obj) {
      var pkg = new ZPackage();
      pkg.Write(obj.m_players.Count);
      foreach (var info in obj.m_players) {
        pkg.Write(info.m_name);
        pkg.Write(info.m_host);
        pkg.Write(info.m_characterID);
        pkg.Write(info.m_publicPosition);
        pkg.Write(info.m_position);
      }
      foreach (var peer in obj.m_peers) {
        if (!peer.IsReady()) continue;
        var rpc = peer.m_rpc;
        if (!obj.m_adminList.Contains(rpc.GetSocket().GetHostName())) continue;
        rpc.Invoke("DEV_PrivatePlayerList", new object[] { pkg });
      }
    }
    public static void Postfix(ZNet __instance) {
      if (!__instance.IsServer() || !Settings.ShowPrivatePlayers) return;
      if (__instance.m_peers.Count == 0) return;
      SendToAdmins(__instance);
    }
  }
  ///<summary>Client side code to receive private players.</summary>
  [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
  public class RegisterRpcPrivatePositions {
    private static void RPC_PrivatePlayerList(ZRpc rpc, ZPackage pkg) {
      if (!Settings.ShowPrivatePlayers) {
        IgnoreDefaultList.Active = false;
        return;
      }
      IgnoreDefaultList.Active = true;
      var obj = ZNet.instance;
      obj.m_players.Clear();
      var length = pkg.ReadInt();
      for (var i = 0; i < length; i++) {
        var playerInfo = new ZNet.PlayerInfo {
          m_name = pkg.ReadString(),
          m_host = pkg.ReadString(),
          m_characterID = pkg.ReadZDOID(),
          m_publicPosition = pkg.ReadBool(),
          m_position = pkg.ReadVector3()
        };
        obj.m_players.Add(playerInfo);
      }
    }
    public static void Postfix(ZNet __instance, ZRpc rpc) {
      if (__instance.IsServer()) return;
      rpc.Register<ZPackage>("DEV_PrivatePlayerList", new Action<ZRpc, ZPackage>(RPC_PrivatePlayerList));
    }
  }
  ///<summary>Two RPC calls modifying the player list may lead to glitches (especially with poor network conditions).</summary>
  [HarmonyPatch(typeof(ZNet), "RPC_PlayerList")]
  public class IgnoreDefaultList {
    public static bool Active = false;
    public static bool Prefix(ZNet __instance, ZRpc rpc) => !Active;
  }
  ///<summary>Remove filtering from the map.</summary>
  [HarmonyPatch(typeof(ZNet), "GetOtherPublicPlayers")]
  public class IncludePrivatePlayersInTheMap {
    public static void Postfix(ZNet __instance, List<ZNet.PlayerInfo> playerList) {
      if (!Settings.ShowPrivatePlayers) return;
      foreach (var playerInfo in __instance.m_players) {
        if (playerInfo.m_publicPosition) continue;
        var characterID = playerInfo.m_characterID;
        if (!characterID.IsNone() && !(playerInfo.m_characterID == __instance.m_characterID)) {
          playerList.Add(playerInfo);
        }
      }
    }
  }
  ///<summary>Simple way to distinguish private players.</summary>
  [HarmonyPatch(typeof(Minimap), "UpdatePlayerPins")]
  public class AddCheckedToPrivatePlayers {
    public static void Postfix(Minimap __instance) {
      if (!Settings.ShowPrivatePlayers) return;
      for (int i = 0; i < __instance.m_tempPlayerInfo.Count; i++) {
        var pin = __instance.m_playerPins[i];
        var info = __instance.m_tempPlayerInfo[i];
        pin.m_checked = !info.m_publicPosition;
      }
    }
  }
}