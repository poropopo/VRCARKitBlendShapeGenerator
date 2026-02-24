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
            var components = context.GetComponentsInChildren<ARKitBlendShapeGeneratorComponent>(avatarRoot, true);

            foreach (var component in components)
            {
                if (component == null) continue;

                // 対象のRendererを取得
                var renderer = component.targetRenderer;
                if (renderer == null)
                {
                    // GetComponentInChildrenは存在しないため、GameObjectから直接取得
                    renderer = component.GetComponentInChildren<SkinnedMeshRenderer>(true);
                }

                if (renderer != null && renderer.sharedMesh != null)
                {
                    yield return RenderGroup.For(renderer).WithData(component);
                }
            }
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

            public RenderAspects WhatChanged => RenderAspects.Mesh | RenderAspects.Shapes;

            public PreviewNode(
                ARKitBlendShapeGeneratorComponent component,
                SkinnedMeshRenderer originalRenderer,
                SkinnedMeshRenderer proxyRenderer,
                ComputeContext context)
            {
                context.Observe(component);

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
            }

            public void Dispose()
            {
                if (_generatedMesh != null)
                {
                    Object.DestroyImmediate(_generatedMesh);
                }
            }
        }
    }
}
