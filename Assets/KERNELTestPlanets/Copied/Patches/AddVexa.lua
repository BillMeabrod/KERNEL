-- Adds Vexa to the default galaxy. Written in the form the SDK's planet inspector writes (CelestialBodyOrbitAuthoring).
PM.Planets:PatchDefaultGalaxy("AddVexa"):Early():First():Do(function(galaxy)
    galaxy:Add("Vexa", function(body)
        body.PrefabKey = ""
        body.referenceBodyGuid = "Kerbol"
        body.OrbitProperties = {
            referenceBodyGuid = "Kerbol",
            inclination = 8,
            eccentricity = 0.1,
            semiMajorAxis = 1500000000000,
            longitudeOfAscendingNode = 40,
            argumentOfPeriapsis = 0,
            meanAnomalyAtEpoch = 3,
            epoch = 0,
        }
        body.OrbiterProperties = {
            orbitColor = { r = 1, g = 0.5, b = 0.3, a = 0.03 },
            nodeColor = { r = 1, g = 0.5, b = 0.3, a = 0.05 },
            lowerCamVsSmaRatio = 0,
            upperCamVsSmaRatio = 0,
            autoTextureOffset = false,
            textureOffset = 0,
        }
    end)
end)
