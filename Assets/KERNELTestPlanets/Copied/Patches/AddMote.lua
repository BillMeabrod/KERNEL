-- Adds Mote to the default galaxy. Written in the form the SDK's planet inspector writes (CelestialBodyOrbitAuthoring).
-- Runs after AddPip: the galaxy lists bodies in the order they're added, and the game creates each body's orbit
-- from its parent, so Pip has to be there first (patches otherwise run in name order).
PM.Planets:PatchDefaultGalaxy("AddMote"):Early():First():AfterPatch("AddPip"):Do(function(galaxy)
    galaxy:Add("Mote", function(body)
        body.PrefabKey = "Celestial.Mote.Local"
        body.referenceBodyGuid = "Pip"
        body.OrbitProperties = {
            referenceBodyGuid = "Pip",
            inclination = 4,
            eccentricity = 0.05,
            semiMajorAxis = 300000,
            longitudeOfAscendingNode = 40,
            argumentOfPeriapsis = 0,
            meanAnomalyAtEpoch = 2,
            epoch = 0,
        }
        body.OrbiterProperties = {
            orbitColor = { r = 0.6, g = 0.65, b = 0.45, a = 0.03 },
            nodeColor = { r = 0.6, g = 0.65, b = 0.45, a = 0.05 },
            lowerCamVsSmaRatio = 0,
            upperCamVsSmaRatio = 0,
            autoTextureOffset = false,
            textureOffset = 0,
        }
    end)
end)
