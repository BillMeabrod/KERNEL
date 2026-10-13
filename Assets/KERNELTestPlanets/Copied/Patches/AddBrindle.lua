-- Adds Brindle to the default galaxy. Written in the form the SDK's planet inspector writes (CelestialBodyOrbitAuthoring).
-- Runs after AddVexa: the galaxy lists bodies in the order they're added, and the game creates each body's orbit
-- from its parent, so Vexa has to be there first (patches otherwise run in name order).
PM.Planets:PatchDefaultGalaxy("AddBrindle"):Early():First():AfterPatch("AddVexa"):Do(function(galaxy)
    galaxy:Add("Brindle", function(body)
        body.PrefabKey = "Celestial.Brindle.Local"
        body.referenceBodyGuid = "Vexa"
        body.OrbitProperties = {
            referenceBodyGuid = "Vexa",
            inclination = 1,
            eccentricity = 0.05,
            semiMajorAxis = 30000000000,
            longitudeOfAscendingNode = 40,
            argumentOfPeriapsis = 0,
            meanAnomalyAtEpoch = 0.5,
            epoch = 0,
        }
        body.OrbiterProperties = {
            orbitColor = { r = 0.85, g = 0.5, b = 0.35, a = 0.03 },
            nodeColor = { r = 0.85, g = 0.5, b = 0.35, a = 0.05 },
            lowerCamVsSmaRatio = 0,
            upperCamVsSmaRatio = 0,
            autoTextureOffset = false,
            textureOffset = 0,
        }
    end)
end)
