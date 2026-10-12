using System.Collections.Generic;
using KSP.IO;
using KSP.Sim;

namespace KERNEL.Ledger
{
    /// <summary>
    /// What each body orbits and how far out, read from the game's galaxy definition. The live lookups need
    /// scene objects that don't exist everywhere, and CelestialBodyProvider.GetPeriapsis is a stub returning 0.
    /// </summary>
    public class GalaxyOrbits
    {
        public readonly struct Orbit
        {
            public readonly string Parent;
            public readonly double SemiMajorAxis;

            public Orbit(string parent, double semiMajorAxis)
            {
                Parent = parent;
                SemiMajorAxis = semiMajorAxis;
            }
        }

        private readonly Dictionary<string, Orbit> _orbits = new();

        // The galaxy definition's Addressables key, e.g. "GalaxyDefinition_Default"
        public string Key { get; }

        public GalaxyOrbits(string key, string json)
        {
            Key = key;
            if (string.IsNullOrEmpty(json)) return;

            var galaxy = IOProvider.FromJson<SerializedGalaxyDefinition>(json);
            if (galaxy?.CelestialBodies == null) return;

            foreach (SerializedCelestialBody body in galaxy.CelestialBodies)
            {
                string parent = body.OrbitProperties?.referenceBodyGuid ?? body.referenceBodyGuid;
                _orbits[body.GUID] = new Orbit(parent ?? string.Empty, body.OrbitProperties?.semiMajorAxis ?? 0d);
            }
        }

        public bool TryGet(string bodyName, out Orbit orbit)
        {
            return _orbits.TryGetValue(bodyName, out orbit);
        }
    }
}
