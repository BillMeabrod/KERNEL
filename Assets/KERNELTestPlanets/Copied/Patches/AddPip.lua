-- Adds Pip to the default galaxy. Written in the form the SDK's planet inspector writes (CelestialBodyOrbitAuthoring).
-- Runs after AddBrindle: the galaxy lists bodies in the order they're added, and the game creates each body's orbit
-- from its parent, so Brindle has to be there first (patches otherwise run in name order).
PM.Planets:PatchDefaultGalaxy("AddPip"):Early():First():AfterPatch("AddBrindle"):Do(function(galaxy)
    galaxy:Add("Pip", function(body)
        body.PrefabKey = "Celestial.Pip.Local"
        body.referenceBodyGuid = "Brindle"
        body.OrbitProperties = {
            referenceBodyGuid = "Brindle",
            inclination = 4,
            eccentricity = 0.05,
            semiMajorAxis = 3000000,
            longitudeOfAscendingNode = 40,
            argumentOfPeriapsis = 0,
            meanAnomalyAtEpoch = 2,
            epoch = 0,
        }
        body.OrbiterProperties = {
            orbitColor = { r = 0.7, g = 0.72, b = 0.75, a = 0.03 },
            nodeColor = { r = 0.7, g = 0.72, b = 0.75, a = 0.05 },
            lowerCamVsSmaRatio = 0,
            upperCamVsSmaRatio = 0,
            autoTextureOffset = false,
            textureOffset = 0,
        }
    end)
end)
