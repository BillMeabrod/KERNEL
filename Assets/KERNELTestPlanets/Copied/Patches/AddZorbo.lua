-- Adds Zorbo to the default galaxy. Written in the form the SDK's planet inspector writes (CelestialBodyOrbitAuthoring).
PM.Planets:PatchDefaultGalaxy("AddZorbo"):Early():First():Do(function(galaxy)
    galaxy:Add("Zorbo", function(body)
        body.PrefabKey = ""
        body.referenceBodyGuid = "Kerbol"
        body.OrbitProperties = {
            referenceBodyGuid = "Kerbol",
            inclination = 2,
            eccentricity = 0.03,
            semiMajorAxis = 80000000000,
            longitudeOfAscendingNode = 40,
            argumentOfPeriapsis = 0,
            meanAnomalyAtEpoch = 1.2,
            epoch = 0,
        }
        body.OrbiterProperties = {
            orbitColor = { r = 0.35, g = 0.75, b = 0.7, a = 0.03 },
            nodeColor = { r = 0.35, g = 0.75, b = 0.7, a = 0.05 },
            lowerCamVsSmaRatio = 0,
            upperCamVsSmaRatio = 0,
            autoTextureOffset = false,
            textureOffset = 0,
        }
    end)
end)
