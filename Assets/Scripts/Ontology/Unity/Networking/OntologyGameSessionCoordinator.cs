using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public enum OntologyGameSessionState
    {
        Booting,
        SignedOut,
        Authenticating,
        Authenticated,
        CharacterSelected,
        WorldSelected,
        EnteringWorld,
        InWorld,
        LeavingWorld,
        Recovering,
        Error
    }

    /// <summary>
    /// Canonical client-side lifecycle for account entry. It does not own account
    /// or world data; it only gates when Unity presentation and runtime adapters
    /// are allowed to run.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyGameSessionCoordinator : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private OntologyGameSessionState state = OntologyGameSessionState.Booting;
        [SerializeField, TextArea] private string lastFailure;

        public OntologyGameSessionState State => state;
        public string LastFailure => lastFailure;
        public bool IsInWorld => state == OntologyGameSessionState.InWorld;
        public bool IsAccountUiState => state != OntologyGameSessionState.InWorld &&
                                        state != OntologyGameSessionState.LeavingWorld;

        public event Action<OntologyGameSessionState, OntologyGameSessionState> StateChanged;

        private void Awake()
        {
            if (authorityClient == null)
                authorityClient = FindAnyObjectByType<OntologyWorldAuthorityClient>();
            SynchronizeFromAuthority();
        }

        public void Bind(OntologyWorldAuthorityClient client)
        {
            authorityClient = client;
            SynchronizeFromAuthority();
        }

        public void SynchronizeFromAuthority()
        {
            if (authorityClient == null || !authorityClient.IsAuthenticated)
            {
                TransitionTo(OntologyGameSessionState.SignedOut);
                return;
            }

            if (authorityClient.HasEnteredCurrentWorld)
            {
                TransitionTo(OntologyGameSessionState.InWorld);
                return;
            }

            if (Guid.TryParse(authorityClient.CurrentWorldId, out _))
            {
                TransitionTo(OntologyGameSessionState.WorldSelected);
                return;
            }

            if (Guid.TryParse(authorityClient.CurrentCharacterId, out _))
            {
                TransitionTo(OntologyGameSessionState.CharacterSelected);
                return;
            }

            TransitionTo(OntologyGameSessionState.Authenticated);
        }

        public void BeginAuthentication() => TransitionTo(OntologyGameSessionState.Authenticating);
        public void AuthenticationCompleted(bool success) =>
            TransitionTo(success ? OntologyGameSessionState.Authenticated : OntologyGameSessionState.SignedOut);
        public void CharacterSelected() => TransitionTo(OntologyGameSessionState.CharacterSelected);
        public void WorldSelected() => TransitionTo(OntologyGameSessionState.WorldSelected);
        public void BeginWorldEntry() => TransitionTo(OntologyGameSessionState.EnteringWorld);
        public void WorldEntryCompleted(bool success)
        {
            if (!success && state == OntologyGameSessionState.Recovering) return;
            TransitionTo(success ? OntologyGameSessionState.InWorld : OntologyGameSessionState.WorldSelected);
        }
        public void BeginLeavingWorld() => TransitionTo(OntologyGameSessionState.LeavingWorld);
        public void SignedOut() => TransitionTo(OntologyGameSessionState.SignedOut);
        public void Recovering() => TransitionTo(OntologyGameSessionState.Recovering);

        public void Fail(string reason)
        {
            lastFailure = reason ?? string.Empty;
            TransitionTo(OntologyGameSessionState.Error);
        }

        private void TransitionTo(OntologyGameSessionState next)
        {
            if (state == next) return;
            var previous = state;
            state = next;
            if (next != OntologyGameSessionState.Error) lastFailure = string.Empty;
            StateChanged?.Invoke(previous, next);
        }
    }
}
