using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace ARKitBlendShapeGenerator
{
    [CustomEditor(typeof(ARKitBlendShapeGeneratorComponent))]
    public class ARKitBlendShapeGeneratorEditor : Editor
    {
        private ARKitBlendShapeGeneratorComponent _component;
        private List<string> _availableBlendShapes = new List<string>();
        private bool _showCustomMappings = true;
        private bool _showAutoMappings = false;
        private bool _showPreview = false;
        private Vector2 _scrollPosition;
        private Vector2 _previewScrollPosition;

        // カテゴリごとの折りたたみ状態
        private bool _foldEye = true;
        private bool _foldEyeLook = true;
        private bool _foldBrow = true;
        private bool _foldMouth = false;
        private bool _foldCheek = false;
        private bool _foldNose = false;
        private bool _foldTongue = false;

        // 検索とフィルタ
        private string _searchFilter = "";
        private int _categoryFilter = 0;
        private static readonly string[] _categoryOptions = {
            "全て", "目 (Eye)", "視線 (Eye Look)", "眉毛 (Brow)",
            "口 (Mouth)", "頬 (Cheek)", "鼻 (Nose)", "舌 (Tongue)"
        };

        private void OnEnable()
        {
            _component = (ARKitBlendShapeGeneratorComponent)target;
            RefreshBlendShapeList();
        }

        private void OnDisable()
        {
            int componentId = _component != null ? _component.GetInstanceID() : 0;
            ARKitBlendShapeGeneratorPreviewState.ReleaseIfActive(componentId);
        }

        private void RefreshBlendShapeList()
        {
            _availableBlendShapes = _component.GetAvailableBlendShapes();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // ヘッダー
            EditorGUILayout.LabelField("ARKit BlendShape Generator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "VRChat/MMDのBlendShapeからARKit用BlendShapeを自動生成します。",
                MessageType.Info);

            EditorGUILayout.Space();

            // 基本設定
            EditorGUILayout.LabelField("基本設定", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetRenderer"));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("BlendShapeリストを更新"))
            {
                RefreshBlendShapeList();
            }
            EditorGUILayout.LabelField($"検出: {_availableBlendShapes.Count}個", GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("intensityMultiplier"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableLeftRightSplit"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("blendWidth"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("overwriteExisting"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            // カスタムマッピング
            _showCustomMappings = EditorGUILayout.Foldout(_showCustomMappings, "カスタムマッピング", true);
            if (_showCustomMappings)
            {
                EditorGUI.indentLevel++;
                DrawCustomMappingsUI();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            // プレビュー
            _showPreview = EditorGUILayout.Foldout(_showPreview, "プレビュー", true);
            if (_showPreview)
            {
                EditorGUI.indentLevel++;
                DrawPreviewUI();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            // 自動マッピング情報
            _showAutoMappings = EditorGUILayout.Foldout(_showAutoMappings, "自動マッピング一覧（参照用）", true);
            if (_showAutoMappings)
            {
                EditorGUI.indentLevel++;
                DrawAutoMappingsInfo();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("debugMode"));

            // デバッグ: 全SkinnedMeshRendererを表示
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全メッシュのBlendShape数を表示", EditorStyles.miniButton))
            {
                var allRenderers = _component.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Debug.Log($"=== 全SkinnedMeshRenderer ({allRenderers.Length}個) ===");
                foreach (var smr in allRenderers)
                {
                    int count = smr.sharedMesh != null ? smr.sharedMesh.blendShapeCount : 0;
                    Debug.Log($"  {smr.gameObject.name}: {count} BlendShapes");
                }
            }
            if (GUILayout.Button("特定メッシュのBlendShape名を表示", EditorStyles.miniButton))
            {
                if (_component.targetRenderer != null && _component.targetRenderer.sharedMesh != null)
                {
                    var mesh = _component.targetRenderer.sharedMesh;
                    Debug.Log($"=== {_component.targetRenderer.gameObject.name} BlendShapes ({mesh.blendShapeCount}個) ===");
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                    {
                        Debug.Log($"  [{i}] {mesh.GetBlendShapeName(i)}");
                    }
                }
                else
                {
                    Debug.LogWarning("Target Renderer not set");
                }
            }
            EditorGUILayout.EndHorizontal();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawNdmfPreviewToggle()
        {
            var isEnabled = ARKitBlendShapeGeneratorPreview.EnableNode.IsEnabled.Value;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("NDMF Preview", EditorStyles.boldLabel, GUILayout.Width(100));

            // プレビュー状態に応じてボタンの色を変更
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = isEnabled ? new Color(0.6f, 1.0f, 0.6f) : new Color(1.0f, 0.6f, 0.6f);

            string buttonText = isEnabled ? "ON" : "OFF";
            if (GUILayout.Button(buttonText, GUILayout.Width(50)))
            {
                ARKitBlendShapeGeneratorPreview.EnableNode.IsEnabled.Value = !isEnabled;
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = originalColor;

            EditorGUILayout.LabelField(isEnabled ? "プレビュー有効" : "プレビュー無効", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCustomMappingsUI()
        {
            EditorGUILayout.HelpBox(
                "自動マッピングできないBlendShape（視線など）を手動で指定できます。\n" +
                "ソースBlendShapeを複数指定して合成することも可能です。",
                MessageType.Info
            );

            // VRChat標準表情のみを使用したプリセット
            EditorGUILayout.LabelField("プリセット", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("VRChat標準表情のみで設定"))
            {
                if (EditorUtility.DisplayDialog(
                    "VRChat標準表情プリセット",
                    "MMD用シェイプキーが存在しないアバター向けに、VRChat標準の表情（vrc.v_aa, vrc.v_ih, vrc.v_ou等）のみを使用したマッピングを設定します。\n\n現在のカスタムマッピングは上書きされます。",
                    "設定する",
                    "キャンセル"))
                {
                    ApplyVRChatStandardPreset();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // カテゴリ別クイック追加ボタン
            EditorGUILayout.LabelField("カテゴリ別追加", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("視線", EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.EyeLook);
            }
            if (GUILayout.Button("目", EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.Eye);
            }
            if (GUILayout.Button("眉毛", EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.Brow);
            }
            if (GUILayout.Button("口", EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.Mouth);
            }
            if (GUILayout.Button("頬", EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.Cheek);
            }
            if (GUILayout.Button("鼻", EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.Nose);
            }
            if (GUILayout.Button("舌", EditorStyles.miniButton))
            {
                AddCategoryMappings(ARKitBlendShapeNames.Tongue);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // 検索フィルタ
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("検索:", GUILayout.Width(40));
            _searchFilter = EditorGUILayout.TextField(_searchFilter);
            if (GUILayout.Button("×", GUILayout.Width(20)))
            {
                _searchFilter = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("新規マッピング追加"))
            {
                AddNewMapping();
            }
            if (GUILayout.Button("全て削除", GUILayout.Width(80)))
            {
                if (EditorUtility.DisplayDialog("確認", "全てのカスタムマッピングを削除しますか？", "はい", "いいえ"))
                {
                    _component.customMappings.Clear();
                    EditorUtility.SetDirty(_component);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // マッピング数表示
            int enabledCount = _component.customMappings.Count(m => m.enabled);
            EditorGUILayout.LabelField($"マッピング: {enabledCount}/{_component.customMappings.Count} 有効");

            // マッピングリスト表示
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.MaxHeight(400));

            for (int i = 0; i < _component.customMappings.Count; i++)
            {
                var mapping = _component.customMappings[i];

                // 検索フィルタ適用
                if (!string.IsNullOrEmpty(_searchFilter))
                {
                    bool matchesArkit = mapping.arkitName.ToLower().Contains(_searchFilter.ToLower());
                    bool matchesSource = mapping.sources.Any(s =>
                        s.blendShapeName != null && s.blendShapeName.ToLower().Contains(_searchFilter.ToLower()));

                    if (!matchesArkit && !matchesSource)
                        continue;
                }

                DrawMappingItem(i);
            }

            EditorGUILayout.EndScrollView();
        }

        private void AddCategoryMappings(string[] arkitNames)
        {
            Undo.RecordObject(_component, "Add Category Mappings");

            foreach (var name in arkitNames)
            {
                if (_component.customMappings.Any(m => m.arkitName == name))
                    continue;

                _component.customMappings.Add(new CustomBlendShapeMapping
                {
                    arkitName = name,
                    enabled = true,
                    sources = new List<BlendShapeSource>()
                });
            }

            EditorUtility.SetDirty(_component);
        }

        private void DrawMappingItem(int index)
        {
            var mapping = _component.customMappings[index];

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // ヘッダー行
            EditorGUILayout.BeginHorizontal();

            mapping.enabled = EditorGUILayout.Toggle(mapping.enabled, GUILayout.Width(20));

            // ARKit名のドロップダウン
            var arkitNames = ARKitBlendShapeNames.GetAll();
            int currentIndex = System.Array.IndexOf(arkitNames, mapping.arkitName);
            if (currentIndex < 0) currentIndex = 0;

            int newIndex = EditorGUILayout.Popup(currentIndex, arkitNames);
            if (newIndex != currentIndex || string.IsNullOrEmpty(mapping.arkitName))
            {
                mapping.arkitName = arkitNames[newIndex];
                EditorUtility.SetDirty(_component);
            }

            if (GUILayout.Button("+", GUILayout.Width(25)))
            {
                mapping.sources.Add(new BlendShapeSource());
                EditorUtility.SetDirty(_component);
            }

            if (GUILayout.Button("×", GUILayout.Width(25)))
            {
                _component.customMappings.RemoveAt(index);
                EditorUtility.SetDirty(_component);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.EndHorizontal();

            // ソースBlendShape
            EditorGUI.indentLevel++;
            for (int j = 0; j < mapping.sources.Count; j++)
            {
                DrawSourceItem(mapping, j);
            }
            EditorGUI.indentLevel--;

            EditorGUILayout.EndVertical();
        }

        private void DrawSourceItem(CustomBlendShapeMapping mapping, int sourceIndex)
        {
            var source = mapping.sources[sourceIndex];

            EditorGUILayout.BeginHorizontal();

            // BlendShape名のドロップダウン
            if (_availableBlendShapes.Count > 0)
            {
                var options = new List<string> { "(選択してください)" };
                options.AddRange(_availableBlendShapes);

                int currentIdx = _availableBlendShapes.IndexOf(source.blendShapeName) + 1;

                // リストにない名前が設定されている場合は末尾に追加して表示
                if (currentIdx == 0 && !string.IsNullOrEmpty(source.blendShapeName))
                {
                    options.Add(source.blendShapeName + " (未検出)");
                    currentIdx = options.Count - 1;
                }

                int newIdx = EditorGUILayout.Popup(currentIdx, options.ToArray());

                if (newIdx > 0 && newIdx < options.Count)
                {
                    // 「(未検出)」付きの項目が選択された場合は元の名前を維持
                    string selectedName = options[newIdx];
                    if (selectedName.EndsWith(" (未検出)"))
                    {
                        selectedName = selectedName.Replace(" (未検出)", "");
                    }

                    if (source.blendShapeName != selectedName)
                    {
                        source.blendShapeName = selectedName;
                        EditorUtility.SetDirty(_component);
                    }
                }
            }
            else
            {
                source.blendShapeName = EditorGUILayout.TextField(source.blendShapeName);
            }

            // 重み
            EditorGUILayout.LabelField("×", GUILayout.Width(15));
            source.weight = EditorGUILayout.Slider(source.weight, -2f, 2f, GUILayout.Width(100));

            // 左右適用範囲
            source.side = (BlendShapeSide)EditorGUILayout.EnumPopup(source.side, GUILayout.Width(70));

            // 削除ボタン
            if (GUILayout.Button("－", GUILayout.Width(25)))
            {
                mapping.sources.RemoveAt(sourceIndex);
                EditorUtility.SetDirty(_component);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void AddNewMapping()
        {
            var newMapping = new CustomBlendShapeMapping
            {
                arkitName = "eyeBlinkLeft",
                enabled = true,
                sources = new List<BlendShapeSource>()
            };
            _component.customMappings.Add(newMapping);
            EditorUtility.SetDirty(_component);
        }

        private void AddEyeLookMappings()
        {
            AddCategoryMappings(ARKitBlendShapeNames.EyeLook);
        }

        /// <summary>
        /// VRChat標準の表情のみを使用したカスタムマッピングを設定
        /// MMD用シェイプキーが存在しないアバター向け
        /// </summary>
        private void ApplyVRChatStandardPreset()
        {
            // BlendShapeリストを先に更新
            RefreshBlendShapeList();

            // SerializedObjectを使用して変更を適用
            serializedObject.Update();

            var customMappingsProperty = serializedObject.FindProperty("customMappings");
            customMappingsProperty.ClearArray();

            // === 目 (Eye) ===
            // vrc.blink または vrc_blink を自動検出
            string blinkName = FindVrcBlendShape("vrc.blink", "vrc_blink");
            AddPresetMappingSerialized(customMappingsProperty, "eyeBlinkLeft", blinkName, 1.0f, BlendShapeSide.LeftOnly);
            AddPresetMappingSerialized(customMappingsProperty, "eyeBlinkRight", blinkName, 1.0f, BlendShapeSide.RightOnly);

            // === 口 - 母音系 (Mouth Vowels) ===
            string vAa = FindVrcBlendShape("vrc.v_aa", "vrc_v_aa");
            string vOu = FindVrcBlendShape("vrc.v_ou", "vrc_v_ou");
            string vIh = FindVrcBlendShape("vrc.v_ih", "vrc_v_ih");
            string vNn = FindVrcBlendShape("vrc.v_nn", "vrc_v_nn");
            string vCh = FindVrcBlendShape("vrc.v_ch", "vrc_v_ch");
            string vOh = FindVrcBlendShape("vrc.v_oh", "vrc_v_oh");

            AddPresetMappingSerialized(customMappingsProperty, "jawOpen", vAa, 0.7f, BlendShapeSide.Both);
            AddPresetMappingSerialized(customMappingsProperty, "mouthFunnel", vOu, 1.0f, BlendShapeSide.Both);
            AddPresetMappingSerialized(customMappingsProperty, "mouthPucker", vOu, 1.2f, BlendShapeSide.Both);
            AddPresetMappingSerialized(customMappingsProperty, "mouthUpperUpLeft", vIh, 1.0f, BlendShapeSide.LeftOnly);
            AddPresetMappingSerialized(customMappingsProperty, "mouthUpperUpRight", vIh, 1.0f, BlendShapeSide.RightOnly);
            AddPresetMappingSerialized(customMappingsProperty, "mouthLowerDownLeft", vAa, 0.6f, BlendShapeSide.LeftOnly);
            AddPresetMappingSerialized(customMappingsProperty, "mouthLowerDownRight", vAa, 0.6f, BlendShapeSide.RightOnly);
            AddPresetMappingSerialized(customMappingsProperty, "mouthClose", vNn, 1.0f, BlendShapeSide.Both);
            AddPresetMappingSerialized(customMappingsProperty, "mouthShrugUpper", vCh, 1.0f, BlendShapeSide.Both);
            AddPresetMappingSerialized(customMappingsProperty, "mouthShrugLower", vOh, 0.5f, BlendShapeSide.Both);
            AddPresetMappingSerialized(customMappingsProperty, "mouthStretchLeft", vIh, 1.0f, BlendShapeSide.LeftOnly);
            AddPresetMappingSerialized(customMappingsProperty, "mouthStretchRight", vIh, 1.0f, BlendShapeSide.RightOnly);
            AddPresetMappingSerialized(customMappingsProperty, "mouthSmileLeft", vIh, 0.7f, BlendShapeSide.LeftOnly);
            AddPresetMappingSerialized(customMappingsProperty, "mouthSmileRight", vIh, 0.7f, BlendShapeSide.RightOnly);

            // === 視線系 (Eye Look) - 無効で追加（通常は手動設定が必要） ===
            foreach (var eyeLookName in ARKitBlendShapeNames.EyeLook)
            {
                AddPresetMappingSerialized(customMappingsProperty, eyeLookName, null, 0f, BlendShapeSide.Both, false);
            }

            serializedObject.ApplyModifiedProperties();

            if (_component.debugMode)
            {
                Debug.Log("[ARKitGenerator] VRChat標準表情プリセットを適用しました");
            }
        }

        /// <summary>
        /// 複数の候補名からメッシュに存在するBlendShape名を検索
        /// </summary>
        private string FindVrcBlendShape(params string[] candidates)
        {
            foreach (var name in candidates)
            {
                if (_availableBlendShapes.Contains(name))
                {
                    return name;
                }
            }
            // 見つからない場合は最初の候補を返す
            return candidates.Length > 0 ? candidates[0] : null;
        }

        /// <summary>
        /// SerializedPropertyを使用してプリセットマッピングを追加
        /// </summary>
        private void AddPresetMappingSerialized(SerializedProperty customMappingsProperty, string arkitName, string sourceName, float weight, BlendShapeSide side, bool enabled = true)
        {
            int index = customMappingsProperty.arraySize;
            customMappingsProperty.InsertArrayElementAtIndex(index);
            var mappingProperty = customMappingsProperty.GetArrayElementAtIndex(index);

            var arkitNameProp = mappingProperty.FindPropertyRelative("arkitName");
            var enabledProp = mappingProperty.FindPropertyRelative("enabled");
            var sourcesProperty = mappingProperty.FindPropertyRelative("sources");

            arkitNameProp.stringValue = arkitName;
            enabledProp.boolValue = enabled;
            sourcesProperty.ClearArray();

            if (!string.IsNullOrEmpty(sourceName))
            {
                sourcesProperty.InsertArrayElementAtIndex(0);
                var sourceProperty = sourcesProperty.GetArrayElementAtIndex(0);

                var blendShapeNameProp = sourceProperty.FindPropertyRelative("blendShapeName");
                var weightProp = sourceProperty.FindPropertyRelative("weight");
                var sideProp = sourceProperty.FindPropertyRelative("side");

                blendShapeNameProp.stringValue = sourceName;
                weightProp.floatValue = weight;
                sideProp.enumValueIndex = (int)side;
            }
        }

        private void DrawPreviewUI()
        {
            // NDMFプレビュー ON/OFF ボタン
            DrawNdmfPreviewToggle();
            EditorGUILayout.Space(5);

            EditorGUILayout.HelpBox(
                "プレビュー表示はNDMF RenderFilterでのみ実行されます。\n" +
                "Inspectorからメッシュを直接書き換える処理は行いません。\n" +
                "そのため targetRenderer のInspector上のBlendShape一覧は増えません（プロキシRenderer側にのみ反映されます）。",
                MessageType.Info);

            var explicitRenderer = _component.targetRenderer;
            var targetRenderer = GetPreviewTargetRenderer();

            if (explicitRenderer != null)
            {
                EditorGUILayout.LabelField("プレビュー対象", $"{explicitRenderer.name} (targetRenderer)");
            }
            else if (targetRenderer != null)
            {
                EditorGUILayout.HelpBox(
                    $"targetRenderer未設定のため、子要素のSkinnedMeshRenderer \"{targetRenderer.name}\" を対象にします。",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "targetRenderer未設定かつ子要素にSkinnedMeshRendererが無いため、NDMFプレビュー対象がありません。",
                    MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                "NDMF PreviewのON/OFFは Tools > NDMf > Preview からも切り替えできます。",
                MessageType.Info);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("リアルタイムプレビュー", EditorStyles.boldLabel);

            int componentId = _component.GetInstanceID();
            ARKitBlendShapeGeneratorPreviewState.BeginEdit(componentId);
            var previewState = ARKitBlendShapeGeneratorPreviewState.Current;
            bool isActive = previewState.InteractiveEnabled && previewState.ActiveComponentInstanceId == componentId;

            if (isActive)
            {
                EditorGUILayout.LabelField("状態", "このコンポーネントがプレビュー対象です");
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "リアルタイムプレビュー対象の初期化待ちです。Inspectorを開き直してください。",
                    MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = isActive;
            if (GUILayout.Button("リセット"))
            {
                ARKitBlendShapeGeneratorPreviewState.SetAllWeights(componentId, new string[0], 0f);
                previewState = ARKitBlendShapeGeneratorPreviewState.Current;
                SceneView.RepaintAll();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (!ARKitBlendShapeGeneratorPreview.EnableNode.IsEnabled.Value)
            {
                EditorGUILayout.HelpBox(
                    "NDMF PreviewがOFFのため、スライダーを変更しても見た目には反映されません。",
                    MessageType.Warning);
            }

            if (!isActive)
            {
                return;
            }

            var previewArkitNames = BuildRealtimePreviewArkitList(targetRenderer);
            if (previewArkitNames.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "プレビュー対象のARKit名を推定できませんでした。カスタムマッピングやソースBlendShapeを確認してください。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(
                $"プレビュー項目: {previewArkitNames.Count} (custom + auto推定)",
                EditorStyles.miniBoldLabel);

            _previewScrollPosition = EditorGUILayout.BeginScrollView(_previewScrollPosition, GUILayout.MaxHeight(260));
            foreach (var arkitName in previewArkitNames)
            {
                float current = previewState.GetWeight(arkitName);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(arkitName, GUILayout.Width(180));
                float next = EditorGUILayout.Slider(current, 0f, 1f);
                EditorGUILayout.EndHorizontal();

                if (Mathf.Abs(next - current) > 0.0001f)
                {
                    ARKitBlendShapeGeneratorPreviewState.SetWeight(componentId, arkitName, next);
                    previewState = ARKitBlendShapeGeneratorPreviewState.Current;
                    SceneView.RepaintAll();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private SkinnedMeshRenderer GetPreviewTargetRenderer()
        {
            if (_component.targetRenderer != null)
            {
                return _component.targetRenderer;
            }

            return _component.GetComponentInChildren<SkinnedMeshRenderer>(true);
        }

        private List<string> BuildRealtimePreviewArkitList(SkinnedMeshRenderer targetRenderer)
        {
            var result = new HashSet<string>();
            var customMappedNames = new HashSet<string>();
            var customMappings = _component.customMappings ?? new List<CustomBlendShapeMapping>();

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

                result.Add(mapping.arkitName);
                customMappedNames.Add(mapping.arkitName);
            }

            if (targetRenderer == null || targetRenderer.sharedMesh == null)
            {
                return result.OrderBy(name => name).ToList();
            }

            var sourceShapeNames = new HashSet<string>();
            for (int i = 0; i < targetRenderer.sharedMesh.blendShapeCount; i++)
            {
                sourceShapeNames.Add(targetRenderer.sharedMesh.GetBlendShapeName(i));
            }

            var processedAutoNames = new HashSet<string>();
            foreach (var mapping in BlendShapeProcessor.GetMappingTable())
            {
                if (mapping == null || string.IsNullOrEmpty(mapping.arkitName) || mapping.sources == null)
                {
                    continue;
                }

                if (customMappedNames.Contains(mapping.arkitName))
                {
                    continue;
                }

                if (processedAutoNames.Contains(mapping.arkitName))
                {
                    continue;
                }

                bool hasAnySource = false;
                foreach (var source in mapping.sources)
                {
                    if (source == null || source.names == null)
                    {
                        continue;
                    }

                    foreach (var sourceName in source.names)
                    {
                        if (!string.IsNullOrEmpty(sourceName) && sourceShapeNames.Contains(sourceName))
                        {
                            hasAnySource = true;
                            break;
                        }
                    }

                    if (hasAnySource)
                    {
                        break;
                    }
                }

                if (!hasAnySource)
                {
                    continue;
                }

                result.Add(mapping.arkitName);
                processedAutoNames.Add(mapping.arkitName);
            }

            return result.OrderBy(name => name).ToList();
        }

        private void DrawAutoMappingsInfo()
        {
            EditorGUILayout.HelpBox(
                "以下のBlendShapeは自動的にマッピングされます。\n" +
                "カスタムマッピングで同じARKit名を指定した場合、カスタム設定が優先されます。",
                MessageType.Info
            );

            // 目
            _foldEye = EditorGUILayout.Foldout(_foldEye, "目 (Eye)");
            if (_foldEye)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("eyeBlinkLeft/Right", "← vrc.blink_left/right, まばたき, ウィンク");
                EditorGUILayout.LabelField("eyeSquintLeft/Right", "← 笑い, にこり, ><");
                EditorGUILayout.LabelField("eyeWideLeft/Right", "← びっくり, 見開き");
                EditorGUI.indentLevel--;
            }

            // 視線
            _foldEyeLook = EditorGUILayout.Foldout(_foldEyeLook, "視線 (Eye Look) ※通常は手動設定が必要");
            if (_foldEyeLook)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("eyeLookUpLeft/Right", "← (手動設定推奨)");
                EditorGUILayout.LabelField("eyeLookDownLeft/Right", "← (手動設定推奨)");
                EditorGUILayout.LabelField("eyeLookInLeft/Right", "← より目");
                EditorGUILayout.LabelField("eyeLookOutLeft/Right", "← (手動設定推奨)");
                EditorGUI.indentLevel--;
            }

            // 眉毛
            _foldBrow = EditorGUILayout.Foldout(_foldBrow, "眉毛 (Brow)");
            if (_foldBrow)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("browDownLeft/Right", "← 怒り, 真面目, 困る");
                EditorGUILayout.LabelField("browInnerUp", "← 困る, 上, 悲しい");
                EditorGUILayout.LabelField("browOuterUpLeft/Right", "← 上, 驚き");
                EditorGUI.indentLevel--;
            }

            // 口
            _foldMouth = EditorGUILayout.Foldout(_foldMouth, "口 (Mouth)");
            if (_foldMouth)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("jawOpen", "← vrc.v_aa, あ (70%)");
                EditorGUILayout.LabelField("mouthFunnel", "← vrc.v_ou, う");
                EditorGUILayout.LabelField("mouthPucker", "← vrc.v_ou, う, ω (120%)");
                EditorGUILayout.LabelField("mouthSmileLeft/Right", "← にやり, ∧, にっこり");
                EditorGUILayout.LabelField("mouthFrownLeft/Right", "← への字, 悲しみ");
                EditorGUI.indentLevel--;
            }

            // 頬
            _foldCheek = EditorGUILayout.Foldout(_foldCheek, "頬 (Cheek)");
            if (_foldCheek)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("cheekPuff", "← ぷく, 膨らみ");
                EditorGUILayout.LabelField("cheekSquintLeft/Right", "← 笑い, にこり");
                EditorGUI.indentLevel--;
            }

            // 鼻
            _foldNose = EditorGUILayout.Foldout(_foldNose, "鼻 (Nose)");
            if (_foldNose)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("noseSneerLeft/Right", "← 怒り");
                EditorGUI.indentLevel--;
            }

            // 舌
            _foldTongue = EditorGUILayout.Foldout(_foldTongue, "舌 (Tongue)");
            if (_foldTongue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("tongueOut", "← べー, 舌");
                EditorGUI.indentLevel--;
            }
        }
    }
}
