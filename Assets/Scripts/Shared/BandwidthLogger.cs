using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace CubeArena.Shared
{
    // Periodically logs Unity Transport's driver-wide send/receive byte counters and
    // current kbit/s bandwidth to Debug.Log — captured in Player.log even in a headless
    // -batchmode -nographics build, which is what makes this usable for the 6-bot load
    // test (see docs/NETCODE.md). NGO's own metrics (NetworkManager.NetworkMetrics) and
    // com.unity.multiplayer.tools' whole NetStats/dispatch layer are internal and
    // unreachable from project code; its Runtime Net Stats Monitor component is a visual
    // UI Toolkit overlay with no public numeric readout, useless headless. UnityTransport.
    // GetNetworkDriver().GetStatistics() (backed by Unity Transport's public
    // DriverStatistics struct) is the one public, loggable source of real byte counts.
    //
    // On a client, the driver has exactly one connection (to the game server), so these
    // numbers ARE that client's own bandwidth. On the server the driver is shared across
    // every connected client, so this instead reports the server's aggregate total across
    // all of them, not a per-client breakdown.
    public static class BandwidthLogger
    {
        public static IEnumerator LogPeriodically(string tag, NetworkManager networkManager, float intervalSeconds)
        {
            var wait = new WaitForSeconds(intervalSeconds);
            while (true)
            {
                yield return wait;

                if (networkManager == null ||
                    networkManager.NetworkConfig == null ||
                    networkManager.NetworkConfig.NetworkTransport is not UnityTransport transport)
                {
                    continue;
                }

                var stats = transport.GetNetworkDriver().GetStatistics();
                Debug.Log($"[BANDWIDTH:{tag}] rxTotal={stats.RxTotalBytes}B txTotal={stats.TxTotalBytes}B " +
                          $"rxCurrent={stats.RxBandwidth.Current:F1}kbit/s txCurrent={stats.TxBandwidth.Current:F1}kbit/s " +
                          $"rxMean={stats.RxBandwidth.Mean:F1}kbit/s txMean={stats.TxBandwidth.Mean:F1}kbit/s");
            }
        }
    }
}
