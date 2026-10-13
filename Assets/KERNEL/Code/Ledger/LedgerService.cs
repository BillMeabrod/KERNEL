using System;
using System.Collections.Generic;
using KSP.Game;
using KSP.Messages;
using UnityEngine;
using ILogger = ReduxLib.Logging.ILogger;

namespace KERNEL.Ledger
{
    /// <summary>
    /// Keeps the science ledger current: loads the save's orbits and rebuilds the ledger when the game reports
    /// something that changes it. Bursts of changes are batched into one rebuild, and rebuilds wait until the
    /// ledger is shown.
    /// </summary>
    public class LedgerService : KerbalMonoBehaviour
    {
        // Batches bursts of events (several reports scored at once) into one rebuild
        private const float RebuildDelay = 0.5f;

        private ILogger _logger;
        private ScienceLedger _ledger;
        private GalaxyOrbits _orbits;
        // Whatever is currently showing the ledger
        private readonly HashSet<object> _viewers = new();

        // Set by events that change the ledger; rebuilt while it's shown, otherwise when it's next shown
        private bool _stale;
        private float _rebuildAt;

        /// <summary>
        /// Every body in the ledger, and the top-level ones (stars) with the rest nested under them.
        /// </summary>
        public IReadOnlyList<BodyEntry> Bodies => _ledger.Bodies;
        public IReadOnlyList<BodyEntry> Roots => _ledger.Roots;

        /// <summary>
        /// Raised after each rebuild with the top-level bodies.
        /// </summary>
        public event Action<IEnumerable<BodyEntry>> Rebuilt;

        /// <summary>
        /// Whether anything is showing the ledger. While something is, changes are rebuilt as they happen.
        /// </summary>
        public bool IsShown => _viewers.Count > 0;

        /// <summary>
        /// Registers or unregisters something showing the ledger (the window, the R&D page). Showing it brings it
        /// up to date first.
        /// </summary>
        public void SetShown(object viewer, bool shown)
        {
            if (shown) _viewers.Add(viewer);
            else _viewers.Remove(viewer);
            if (shown && _stale) Rebuild();
        }

        public void Initialize(ILogger logger)
        {
            _logger = logger;
            _ledger = new ScienceLedger(logger);

            if (Messages == null)
            {
                _logger.LogError("Message center isn't available; the ledger won't update");
                return;
            }

            Messages.PersistentSubscribe<GameLoadFinishedMessage>(OnGameLoadFinished);
            // Scoring changes earned science and can reveal a "?????" biome
            Messages.PersistentSubscribe<ResearchReportScoredMessage>(MarkStale);
            // New tech can unlock experiments. Only whole tiers send a message, but nodes can only be unlocked on
            // the R&D screen, so the scene change when leaving it covers single nodes
            Messages.PersistentSubscribe<TechTierUnlockedMessage>(MarkStale);
            Messages.PersistentSubscribe<GameStateChangedMessage>(MarkStale);
            // Possible science scales with the difficulty's science rewards
            Messages.PersistentSubscribe<DifficultyOptionStatesChangedMessage>(MarkStale);
        }

        private void OnGameLoadFinished(MessageCenterMessage message)
        {
            // Orbit parents and distances come from the save's galaxy definition, which only changes with the galaxy
            string galaxyKey = Game.SessionManager.ActiveGalaxyDefinitionKey;
            if (_orbits != null && _orbits.Key == galaxyKey)
            {
                Rebuild();
                return;
            }

            Assets.Load<TextAsset>(galaxyKey, asset =>
            {
                if (asset == null)
                {
                    _logger.LogError($"Could not load galaxy definition '{galaxyKey}'; bodies won't be nested or ordered");
                    _orbits = new GalaxyOrbits(galaxyKey, null);
                }
                else
                {
                    _orbits = new GalaxyOrbits(galaxyKey, asset.text);
                    Assets.ReleaseAsset(asset);
                }

                Rebuild();
            });
        }

        private void MarkStale(MessageCenterMessage message)
        {
            // Nothing to refresh before the first game load
            if (_orbits == null) return;

            if (!_stale) _rebuildAt = Time.unscaledTime + RebuildDelay;
            _stale = true;
        }

        private void Update()
        {
            if (_stale && IsShown && Time.unscaledTime >= _rebuildAt)
                Rebuild();
        }

        private void Rebuild()
        {
            _stale = false;
            if (!_ledger.Rebuild(Game, _orbits)) return;

            _logger.LogInfo($"Ledger built: {_ledger.Bodies.Count} bodies");
            Rebuilt?.Invoke(_ledger.Roots);
        }
    }
}
