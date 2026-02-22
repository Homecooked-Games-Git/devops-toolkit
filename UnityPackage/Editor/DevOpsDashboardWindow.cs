using UnityEditor;
using UnityEngine;

namespace HomecookedGames.DevOps.Editor
{
    public class DevOpsDashboardWindow : EditorWindow
    {
        [SerializeField] int selectedTab;

        StatusChecker _checker;
        ProcessRunner _runner;
        CICDTab _cicdTab;
        EssentialsTab _essentialsTab;
        Vector2 _scrollPos;

        static readonly string[] TabNames = { "CI/CD", "Essentials" };

        [MenuItem("HCTools/DevOps Dashboard")]
        static void ShowWindow()
        {
            var window = GetWindow<DevOpsDashboardWindow>();
            window.titleContent = new GUIContent("DevOps Dashboard");
            window.minSize = new Vector2(480f, 400f);
            window.Show();
        }

        void OnEnable()
        {
            _checker = new StatusChecker();
            _runner = new ProcessRunner();
            _runner.SetRepaintCallback(Repaint);

            _cicdTab = new CICDTab(_checker, _runner);
            _cicdTab.OnEnable();

            _essentialsTab = new EssentialsTab();
            _essentialsTab.SetRepaintCallback(Repaint);
            _essentialsTab.OnEnable();
        }

        void OnGUI()
        {
            // Header
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            selectedTab = GUILayout.Toolbar(selectedTab, TabNames, EditorStyles.toolbarButton);

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                _checker.Refresh();
                _essentialsTab.OnEnable();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            // Tab content
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            switch (selectedTab)
            {
                case 0:
                    _cicdTab.OnGUI();
                    break;
                case 1:
                    _essentialsTab.OnGUI();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
