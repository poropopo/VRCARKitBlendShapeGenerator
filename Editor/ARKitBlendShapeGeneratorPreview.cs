using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf;
using nadena.dev.ndmf.preview;
using UnityEngine;

namespace ARKitBlendShapeGenerator
{
    /// <summary>
    /// NDMFプレビューシステム統合
    /// Tools > NDMf > Preview でON/OFFを切り替え可能
    /// </summary>
    public class ARKitBlendShapeGeneratorPreview : IRenderFilter
    {
        /// <summary>
        /// プレビューのON/OFFを制御するノード
        /// NDMFのPreviewメニューに表示される
        /// </summary>
        public static readonly TogglablePreviewNode EnableNode = TogglablePreviewNode.Create(
            () => "ARKit BlendShape Generator",
            qualifiedName: "com.qazx7412.kx-vrc-arkit-blendshape-generator/Preview",
            initialState: false
        );

        public IEnumerable<TogglablePreviewNode> GetPreviewControlNodes()
        {
            yield return EnableNode;
        }

        public bool IsEnabled(ComputeContext context)
        {
            return context.Observe(EnableNode.IsEnabled);
        }

        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            var avatarRoots = context.GetAvatarRoots();
            return avatarRoots.SelectMany(r => GroupsForAvatar(context, r)).ToImmutableList();
        }

        private IEnumerable<RenderGroup> GroupsForAvatar(ComputeContext context, GameObject avatarRoot)
        {
            // このアバターにARKitBlendShapeGeneratorComponentがあるか確認
            var components = context
                .GetComponentsInChildren<ARKitBlendShapeGeneratorComponent>(avatarRoot, true)
                .Where(c => c != null)
                .ToArray();
            var component = SelectPrimaryComponent(avatarRoot, components);

            if (component != null)
            {
                // targetRenderer変更に追従させる
                var renderer = context.Observe(component, c => c.targetRenderer);
                if (renderer == null)
                {
                    // targetRenderer未設定時は子要素をフォールバック対象にする
                    renderer = context
                        .GetComponentsInChildren<SkinnedMeshRenderer>(component.gameObject, true)
                        .FirstOrDefault();
                }

                if (renderer != null && context.Observe(renderer, r => r.sharedMesh) != null)
                {
                    yield return RenderGroup.For(renderer).WithData(component);
                }
            }
        }

        private static ARKitBlendShapeGeneratorComponent SelectPrimaryComponent(
            GameObject avatarRoot,
            ARKitBlendShapeGeneratorComponent[] components)
        {
            if (components == null || components.Length == 0)
            {
                return null;
            }

            var onRoot = components.FirstOrDefault(c => c != null && c.gameObject == avatarRoot);
            if (onRoot != null)
            {
                return onRoot;
            }

            return components[0];
        }

        public Task<IRenderFilterNode> Instantiate(
            RenderGroup group,
            IEnumerable<(Renderer, Renderer)> proxyPairs,
            ComputeContext context)
        {
            var (original, proxy) = proxyPairs.First();
            var component = group.GetData<ARKitBlendShapeGeneratorComponent>();

            if (original is not SkinnedMeshRenderer originalSmr ||
                proxy is not SkinnedMeshRenderer proxySmr)
            {
                return Task.FromResult<IRenderFilterNode>(null);
            }

            var node = new PreviewNode(component, originalSmr, proxySmr, context);
            return Task.FromResult<IRenderFilterNode>(node);
        }

        /// <summary>
        /// プレビューノード - 実際のBlendShape生成処理を行う
        /// </summary>
        private class PreviewNode : IRenderFilterNode
        {
            private Mesh _generatedMesh;
            private readonly int _componentInstanceId;
            private readonly Dictionary<string, int> _shapeIndices = new Dictionary<string, int>();
            private readonly HashSet<int> _appliedInteractiveIndices = new HashSet<int>();

            public RenderAspects WhatChanged => RenderAspects.Mesh | RenderAspects.Shapes;

            public PreviewNode(
                ARKitBlendShapeGeneratorComponent component,
                SkinnedMeshRenderer originalRenderer,
                SkinnedMeshRenderer proxyRenderer,
                ComputeContext context)
            {
                _componentInstanceId = component != null ? component.GetInstanceID() : 0;

                // customMappingsの内容を含むコンポーネント変更全体を監視
                context.Observe(component);
                context.Observe(component, c => c.intensityMultiplier);
                context.Observe(component, c => c.enableLeftRightSplit);
                context.Observe(component, c => c.blendWidth);
                context.Observe(component, c => c.overwriteExisting);
                context.Observe(component, c => c.targetRenderer);

                context.Observe(originalRenderer, r => r.sharedMesh);
                context.Observe(proxyRenderer, r => r.sharedMesh);

                var sourceMesh = proxyRenderer.sharedMesh ?? originalRenderer.sharedMesh;
                if (sourceMesh == null)
                {
                    return;
                }

                _generatedMesh = Object.Instantiate(sourceMesh);
                _generatedMesh.name = sourceMesh.name + "_ARKitPreview";

                BlendShapeGenerationEngine.Generate(
                    sourceMesh,
                    _generatedMesh,
                    component.customMappings,
                    BlendShapeProcessor.GetMappingTable(),
                    BlendShapeGenerationOptions.FromComponent(component));

                CacheShapeIndices(_generatedMesh);
                proxyRenderer.sharedMesh = _generatedMesh;
            }

            public Task<IRenderFilterNode> Refresh(
                IEnumerable<(Renderer, Renderer)> proxyPairs,
                ComputeContext context,
                RenderAspects updatedAspects)
            {
                // メッシュが変わった場合は再生成が必要
                if ((updatedAspects & RenderAspects.Mesh) != 0)
                {
                    return Task.FromResult<IRenderFilterNode>(null);
                }

                return Task.FromResult<IRenderFilterNode>(this);
            }

            public void OnFrame(Renderer original, Renderer proxy)
            {
                if (_generatedMesh == null || proxy is not SkinnedMeshRenderer proxySmr) return;

                // プロキシのメッシュが正しいか確認
                if (proxySmr.sharedMesh != _generatedMesh)
                {
                    proxySmr.sharedMesh = _generatedMesh;
                }

                var interactiveState = ARKitBlendShapeGeneratorPreviewState.Current;
                if (!interactiveState.InteractiveEnabled ||
                    interactiveState.ActiveComponentInstanceId != _componentInstanceId)
                {
                    ClearAppliedInteractiveWeights(proxySmr);
                    return;
                }

                var nextAppliedIndices = new HashSet<int>();
                foreach (var kvp in interactiveState.WeightsByArkitName)
                {
                    if (!_shapeIndices.TryGetValue(kvp.Key, out int blendShapeIndex))
                    {
                        continue;
                    }

                    float clamped = Mathf.Clamp01(kvp.Value) * 100f;
                    if (clamped <= 0.0001f)
                    {
                        continue;
                    }

                    proxySmr.SetBlendShapeWeight(blendShapeIndex, clamped);
                    nextAppliedIndices.Add(blendShapeIndex);
                }

                foreach (int previouslyAppliedIndex in _appliedInteractiveIndices)
                {
                    if (!nextAppliedIndices.Contains(previouslyAppliedIndex))
                    {
                        proxySmr.SetBlendShapeWeight(previouslyAppliedIndex, 0f);
                    }
                }

                _appliedInteractiveIndices.Clear();
                foreach (int appliedIndex in nextAppliedIndices)
                {
                    _appliedInteractiveIndices.Add(appliedIndex);
                }
            }

            private void CacheShapeIndices(Mesh mesh)
            {
                _shapeIndices.Clear();
                if (mesh == null)
                {
                    return;
                }

                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string shapeName = mesh.GetBlendShapeName(i);
                    if (string.IsNullOrEmpty(shapeName) || _shapeIndices.ContainsKey(shapeName))
                    {
                        continue;
                    }

                    _shapeIndices.Add(shapeName, i);
                }
            }

            public void Dispose()
            {
                _appliedInteractiveIndices.Clear();

                if (_generatedMesh != null)
                {
                    Object.DestroyImmediate(_generatedMesh);
                }
            }

            private void ClearAppliedInteractiveWeights(SkinnedMeshRenderer proxySmr)
            {
                foreach (int index in _appliedInteractiveIndices)
                {
                    proxySmr.SetBlendShapeWeight(index, 0f);
                }

                _appliedInteractiveIndices.Clear();
            }
        }
    }
}
