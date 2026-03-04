using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ARKitBlendShapeGenerator
{
    internal sealed class BlendShapeGenerationOptions
    {
        public float IntensityMultiplier { get; set; } = 1.0f;
        public bool EnableLeftRightSplit { get; set; } = true;
        public float BlendWidth { get; set; } = 0.02f;
        public bool OverwriteExisting { get; set; }
        public bool Debug { get; set; }

        public static BlendShapeGenerationOptions FromComponent(ARKitBlendShapeGeneratorComponent component)
        {
            return new BlendShapeGenerationOptions
            {
                IntensityMultiplier = component.intensityMultiplier,
                EnableLeftRightSplit = component.enableLeftRightSplit,
                BlendWidth = component.blendWidth,
                OverwriteExisting = component.overwriteExisting,
                Debug = component.debugMode
            };
        }
    }

    internal sealed class BlendShapeGenerationResult
    {
        public List<string> GeneratedShapes { get; }
        public Dictionary<string, int> ShapeIndices { get; }

        public BlendShapeGenerationResult(List<string> generatedShapes, Dictionary<string, int> shapeIndices)
        {
            GeneratedShapes = generatedShapes;
            ShapeIndices = shapeIndices;
        }
    }

    internal static class BlendShapeGenerationEngine
    {
        private sealed class BlendShapeFrameData
        {
            public readonly float Weight;
            public readonly Vector3[] DeltaVertices;
            public readonly Vector3[] DeltaNormals;
            public readonly Vector3[] DeltaTangents;

            public BlendShapeFrameData(
                float weight,
                Vector3[] deltaVertices,
                Vector3[] deltaNormals,
                Vector3[] deltaTangents)
            {
                Weight = weight;
                DeltaVertices = deltaVertices;
                DeltaNormals = deltaNormals;
                DeltaTangents = deltaTangents;
            }
        }

        private sealed class BlendShapeData
        {
            public readonly string Name;
            public readonly List<BlendShapeFrameData> Frames;

            public BlendShapeData(string name, List<BlendShapeFrameData> frames)
            {
                Name = name;
                Frames = frames;
            }
        }

        public static BlendShapeGenerationResult Generate(
            Mesh sourceMesh,
            Mesh targetMesh,
            List<CustomBlendShapeMapping> customMappings,
            List<ARKitMapping> autoMappings,
            BlendShapeGenerationOptions options)
        {
            if (sourceMesh == null || targetMesh == null)
            {
                return new BlendShapeGenerationResult(
                    new List<string>(),
                    new Dictionary<string, int>());
            }

            if (options == null)
            {
                options = new BlendShapeGenerationOptions();
            }

            if (customMappings == null)
            {
                customMappings = new List<CustomBlendShapeMapping>();
            }

            if (autoMappings == null)
            {
                autoMappings = new List<ARKitMapping>();
            }

            var existingShapes = new Dictionary<string, int>();
            for (int i = 0; i < sourceMesh.blendShapeCount; i++)
            {
                existingShapes[sourceMesh.GetBlendShapeName(i)] = i;
            }

            var generatedShapes = new List<string>();
            var customMappedNames = new HashSet<string>();

            ProcessCustomMappings(
                sourceMesh,
                targetMesh,
                customMappings,
                options,
                existingShapes,
                customMappedNames,
                generatedShapes);

            ProcessAutoMappings(
                sourceMesh,
                targetMesh,
                autoMappings,
                options,
                existingShapes,
                customMappedNames,
                generatedShapes);

            return new BlendShapeGenerationResult(generatedShapes, existingShapes);
        }

        private static void ProcessCustomMappings(
            Mesh sourceMesh,
            Mesh targetMesh,
            List<CustomBlendShapeMapping> customMappings,
            BlendShapeGenerationOptions options,
            Dictionary<string, int> existingShapes,
            HashSet<string> customMappedNames,
            List<string> generatedShapes)
        {
            foreach (var mapping in customMappings)
            {
                if (mapping == null || !mapping.enabled || string.IsNullOrEmpty(mapping.arkitName))
                {
                    continue;
                }

                if (mapping.sources == null || mapping.sources.Count == 0)
                {
                    continue;
                }

                customMappedNames.Add(mapping.arkitName);

                if (existingShapes.ContainsKey(mapping.arkitName) && !options.OverwriteExisting)
                {
                    Log(options, $"Skip custom (exists): {mapping.arkitName}");
                    continue;
                }

                var sources = new List<(int index, float weight, BlendShapeSide side)>();
                foreach (var source in mapping.sources)
                {
                    if (source == null || string.IsNullOrEmpty(source.blendShapeName))
                    {
                        continue;
                    }

                    if (TryGetSourceIndex(existingShapes, sourceMesh, source.blendShapeName, out int srcIndex))
                    {
                        sources.Add((srcIndex, source.weight, source.side));
                    }
                    else
                    {
                        Log(options, $"Warning: Source not found: {source.blendShapeName} for {mapping.arkitName}");
                    }
                }

                if (sources.Count == 0)
                {
                    Log(options, $"Skip custom (no valid source): {mapping.arkitName}");
                    continue;
                }

                if (TryAddBlendShape(sourceMesh, targetMesh, mapping.arkitName, sources, options))
                {
                    existingShapes[mapping.arkitName] = targetMesh.blendShapeCount - 1;
                    generatedShapes.Add(mapping.arkitName);
                }
            }
        }

        private static void ProcessAutoMappings(
            Mesh sourceMesh,
            Mesh targetMesh,
            List<ARKitMapping> autoMappings,
            BlendShapeGenerationOptions options,
            Dictionary<string, int> existingShapes,
            HashSet<string> customMappedNames,
            List<string> generatedShapes)
        {
            var processedArkitNames = new HashSet<string>();

            foreach (var mapping in autoMappings)
            {
                if (mapping == null || string.IsNullOrEmpty(mapping.arkitName) || mapping.sources == null)
                {
                    continue;
                }

                if (customMappedNames.Contains(mapping.arkitName))
                {
                    Log(options, $"Skip auto (custom defined): {mapping.arkitName}");
                    continue;
                }

                if (processedArkitNames.Contains(mapping.arkitName))
                {
                    Log(options, $"Skip auto (already generated in this pass): {mapping.arkitName}");
                    continue;
                }

                if (existingShapes.ContainsKey(mapping.arkitName) && !options.OverwriteExisting)
                {
                    Log(options, $"Skip auto (exists in source): {mapping.arkitName}");
                    continue;
                }

                var sources = FindAutoSources(mapping.sources, existingShapes, sourceMesh);
                if (sources.Count == 0)
                {
                    Log(options, $"Skip auto (no source): {mapping.arkitName}");
                    continue;
                }

                var side = options.EnableLeftRightSplit ? mapping.side : BlendShapeSide.Both;
                var sourcesWithSide = sources.Select(s => (s.index, s.weight, side)).ToList();

                if (TryAddBlendShape(sourceMesh, targetMesh, mapping.arkitName, sourcesWithSide, options))
                {
                    existingShapes[mapping.arkitName] = targetMesh.blendShapeCount - 1;
                    generatedShapes.Add(mapping.arkitName);
                    processedArkitNames.Add(mapping.arkitName);
                }
            }
        }

        private static List<(int index, float weight)> FindAutoSources(
            List<SourceMapping> sourceMappings,
            Dictionary<string, int> existingShapes,
            Mesh sourceMesh)
        {
            var result = new List<(int index, float weight)>();

            foreach (var sourceMapping in sourceMappings)
            {
                if (sourceMapping == null || sourceMapping.names == null)
                {
                    continue;
                }

                foreach (var name in sourceMapping.names)
                {
                    if (TryGetSourceIndex(existingShapes, sourceMesh, name, out int srcIndex))
                    {
                        result.Add((srcIndex, sourceMapping.weight));
                        break;
                    }
                }
            }

            return result;
        }

        private static bool TryGetSourceIndex(
            Dictionary<string, int> existingShapes,
            Mesh sourceMesh,
            string sourceName,
            out int srcIndex)
        {
            srcIndex = -1;
            if (string.IsNullOrEmpty(sourceName))
            {
                return false;
            }

            if (!existingShapes.TryGetValue(sourceName, out int index))
            {
                return false;
            }

            if (index < 0 || index >= sourceMesh.blendShapeCount)
            {
                return false;
            }

            srcIndex = index;
            return true;
        }

        private static bool TryAddBlendShape(
            Mesh sourceMesh,
            Mesh targetMesh,
            string arkitName,
            List<(int index, float weight, BlendShapeSide side)> sources,
            BlendShapeGenerationOptions options)
        {
            int vertexCount = sourceMesh.vertexCount;
            var deltaVertices = new Vector3[vertexCount];
            var deltaNormals = new Vector3[vertexCount];
            var deltaTangents = new Vector3[vertexCount];
            var vertices = sourceMesh.vertices;

            int sourceCount = 0;
            float blendWidth = Mathf.Max(0.0001f, options.BlendWidth);

            foreach (var (index, weight, side) in sources)
            {
                if (index < 0 || index >= sourceMesh.blendShapeCount)
                {
                    continue;
                }

                int frameCount = sourceMesh.GetBlendShapeFrameCount(index);
                if (frameCount == 0)
                {
                    continue;
                }

                var srcDeltaV = new Vector3[vertexCount];
                var srcDeltaN = new Vector3[vertexCount];
                var srcDeltaT = new Vector3[vertexCount];

                int targetFrame = frameCount - 1;
                sourceMesh.GetBlendShapeFrameVertices(index, targetFrame, srcDeltaV, srcDeltaN, srcDeltaT);

                float adjustedWeight = weight * options.IntensityMultiplier;
                for (int i = 0; i < vertexCount; i++)
                {
                    float sideMultiplier = 1.0f;
                    if (options.EnableLeftRightSplit && side != BlendShapeSide.Both)
                    {
                        float vertexX = vertices[i].x;

                        if (side == BlendShapeSide.LeftOnly)
                        {
                            if (vertexX > blendWidth)
                            {
                                sideMultiplier = 0.0f;
                            }
                            else if (vertexX > -blendWidth)
                            {
                                sideMultiplier = (blendWidth - vertexX) / (blendWidth * 2.0f);
                            }
                        }
                        else if (side == BlendShapeSide.RightOnly)
                        {
                            if (vertexX < -blendWidth)
                            {
                                sideMultiplier = 0.0f;
                            }
                            else if (vertexX < blendWidth)
                            {
                                sideMultiplier = (vertexX + blendWidth) / (blendWidth * 2.0f);
                            }
                        }
                    }

                    if (sideMultiplier > 0.0f)
                    {
                        float finalWeight = adjustedWeight * sideMultiplier;
                        deltaVertices[i] += srcDeltaV[i] * finalWeight;
                        deltaNormals[i] += srcDeltaN[i] * finalWeight;
                        deltaTangents[i] += srcDeltaT[i] * finalWeight;
                    }
                }

                sourceCount++;
            }

            if (sourceCount == 0)
            {
                return false;
            }

            if (options.OverwriteExisting && ContainsBlendShape(targetMesh, arkitName))
            {
                if (RemoveBlendShapeByName(targetMesh, arkitName))
                {
                    Log(options, $"Replaced existing blendshape: {arkitName}");
                }
            }

            targetMesh.AddBlendShapeFrame(arkitName, 100f, deltaVertices, deltaNormals, deltaTangents);
            Log(options, $"Generated: {arkitName} from {sourceCount} source(s)");
            return true;
        }

        private static bool ContainsBlendShape(Mesh mesh, string shapeName)
        {
            if (mesh == null || string.IsNullOrEmpty(shapeName))
            {
                return false;
            }

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                if (mesh.GetBlendShapeName(i) == shapeName)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RemoveBlendShapeByName(Mesh mesh, string shapeName)
        {
            if (mesh == null || string.IsNullOrEmpty(shapeName))
            {
                return false;
            }

            int blendShapeCount = mesh.blendShapeCount;
            if (blendShapeCount == 0)
            {
                return false;
            }

            int vertexCount = mesh.vertexCount;
            bool removed = false;
            var preserved = new List<BlendShapeData>(blendShapeCount);

            for (int shapeIndex = 0; shapeIndex < blendShapeCount; shapeIndex++)
            {
                string existingName = mesh.GetBlendShapeName(shapeIndex);
                if (string.IsNullOrEmpty(existingName))
                {
                    continue;
                }

                if (existingName == shapeName)
                {
                    removed = true;
                    continue;
                }

                int frameCount = mesh.GetBlendShapeFrameCount(shapeIndex);
                var frames = new List<BlendShapeFrameData>(frameCount);
                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    float frameWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, frameIndex);
                    var deltaVertices = new Vector3[vertexCount];
                    var deltaNormals = new Vector3[vertexCount];
                    var deltaTangents = new Vector3[vertexCount];

                    mesh.GetBlendShapeFrameVertices(
                        shapeIndex,
                        frameIndex,
                        deltaVertices,
                        deltaNormals,
                        deltaTangents);

                    frames.Add(new BlendShapeFrameData(
                        frameWeight,
                        deltaVertices,
                        deltaNormals,
                        deltaTangents));
                }

                preserved.Add(new BlendShapeData(existingName, frames));
            }

            if (!removed)
            {
                return false;
            }

            mesh.ClearBlendShapes();
            foreach (var shape in preserved)
            {
                foreach (var frame in shape.Frames)
                {
                    mesh.AddBlendShapeFrame(
                        shape.Name,
                        frame.Weight,
                        frame.DeltaVertices,
                        frame.DeltaNormals,
                        frame.DeltaTangents);
                }
            }

            return true;
        }

        private static void Log(BlendShapeGenerationOptions options, string message)
        {
            if (options != null && options.Debug)
            {
                Debug.Log($"[ARKitGenerator] {message}");
            }
        }
    }
}
