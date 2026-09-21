using System;
using System.Collections.Generic;
using System.Text;
using CubeArena.Shared;
using Unity.Netcode;
using UnityEngine;

namespace CubeArena.Server
{
    // Wires TicketValidator into NGO's ConnectionApprovalCallback, per section 3.2's
    // four checks: signature+exp, aud/sid, jti replay, and current player count < capacity.
    public class ConnectionApprovalHandler
    {
        private readonly TicketValidator _validator;
        private readonly Guid _sessionId;
        private readonly int _capacity;
        private readonly NetworkManager _networkManager;
        private readonly HashSet<string> _usedJti = new();

        public event Action<ulong, Guid, int> ClientApproved;
        public event Action<ConnectRejectionReason> ConnectionRejected;

        public ConnectionApprovalHandler(TicketValidator validator, NetworkManager networkManager, Guid sessionId, int capacity)
        {
            _validator = validator;
            _networkManager = networkManager;
            _sessionId = sessionId;
            _capacity = capacity;
        }

        public void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            var ticket = Encoding.UTF8.GetString(request.Payload ?? Array.Empty<byte>());
            var result = _validator.Validate(ticket, _sessionId, _usedJti, DateTimeOffset.UtcNow);

            if (!result.IsValid)
            {
                Reject(response, result.RejectionReason);
                return;
            }

            if (_networkManager.ConnectedClientsIds.Count >= _capacity)
            {
                Reject(response, ConnectRejectionReason.ServerFull);
                return;
            }

            if (result.SlotIndex < 0 || result.SlotIndex >= _capacity)
            {
                Reject(response, ConnectRejectionReason.InvalidSlot);
                return;
            }

            _usedJti.Add(result.Jti);

            response.Approved = true;
            response.CreatePlayerObject = false;
            response.Pending = false;

            Debug.Log($"[ConnectionApproval] Approved client {request.ClientNetworkId}: user={result.UserId} slot={result.SlotIndex}");
            ClientApproved?.Invoke(request.ClientNetworkId, result.UserId, result.SlotIndex);
        }

        private void Reject(NetworkManager.ConnectionApprovalResponse response, ConnectRejectionReason reason)
        {
            response.Approved = false;
            response.CreatePlayerObject = false;
            response.Pending = false;
            response.Reason = reason.ToString();

            Debug.LogWarning($"[ConnectionApproval] Rejected connection: {reason}");
            ConnectionRejected?.Invoke(reason);
        }
    }
}
