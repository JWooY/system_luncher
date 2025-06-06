using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEngine;
using UnityEditor;
using UnityEditor.VersionControl;

using Codice.Client.Common.Threading;
using Codice.CM.Common;
using Codice.LogWrapper;
using PlasticGui;
using PlasticGui.WorkspaceWindow;
using Unity.PlasticSCM.Editor.AssetMenu;
using Unity.PlasticSCM.Editor.AssetsOverlays;
using Unity.PlasticSCM.Editor.AssetsOverlays.Cache;
using Unity.PlasticSCM.Editor.AssetUtils;
using Unity.PlasticSCM.Editor.AssetUtils.Processor;
using Unity.PlasticSCM.Editor.UI;
using Unity.PlasticSCM.Editor.Tool;

namespace Unity.PlasticSCM.Editor.Inspector
{
    static class DrawInspectorOperations
    {
        internal static void Enable(
            WorkspaceInfo wkInfo,
            IPlasticAPI plasticApi,
            IAssetStatusCache assetStatusCache)
        {
            if (mIsEnabled)
                return;

            mLog.Debug("Enable");

            mWkInfo = wkInfo;
            mPlasticAPI = plasticApi;
            mAssetStatusCache = assetStatusCache;

            mIsEnabled = true;

            mAssetSelection = new InspectorAssetSelection();

            UnityEditor.Editor.finishedDefaultHeaderGUI +=
                Editor_finishedDefaultHeaderGUI;

            RepaintInspector.All();
        }

        internal static void Disable()
        {
            mLog.Debug("Disable");

            mIsEnabled = false;

            UnityEditor.Editor.finishedDefaultHeaderGUI -=
                Editor_finishedDefaultHeaderGUI;

            RepaintInspector.All();

            mWkInfo = null;
            mAssetStatusCache = null;
            mAssetSelection = null;
            mOperations = null;
        }

        internal static void BuildOperations(
            WorkspaceInfo wkInfo,
            IPlasticAPI plasticApi,
            WorkspaceWindow workspaceWindow,
            IViewSwitcher viewSwitcher,
            IHistoryViewLauncher historyViewLauncher,
            GluonGui.ViewHost viewHost,
            WorkspaceOperationsMonitor workspaceOperationsMonitor,
            ISaveAssets saveAssets,
            PlasticGui.WorkspaceWindow.NewIncomingChangesUpdater incomingChangesUpdater,
            ShelvedChangesUpdater shelvedChangesUpdater,
            IAssetStatusCache assetStatusCache,
            IMergeViewLauncher mergeViewLauncher,
            PlasticGui.Gluon.IGluonViewSwitcher gluonViewSwitcher,
            LaunchTool.IShowDownloadPlasticExeWindow showDownloadPlasticExeWindow,
            bool isGluonMode)
        {
            if (!mIsEnabled)
                Enable(wkInfo, plasticApi, assetStatusCache);

            mOperations = new AssetVcsOperations(
                wkInfo,
                plasticApi,
                workspaceWindow,
                viewSwitcher,
                historyViewLauncher,
                viewHost,
                workspaceOperationsMonitor,
                saveAssets,
                incomingChangesUpdater,
                shelvedChangesUpdater,
                mAssetStatusCache,
                mergeViewLauncher,
                gluonViewSwitcher,
                mAssetSelection,
                showDownloadPlasticExeWindow,
                isGluonMode);
        }

        static void Editor_finishedDefaultHeaderGUI(UnityEditor.Editor inspector)
        {
            try
            {
                if (!mIsEnabled)
                    return;

                mAssetSelection.SetActiveInspector(inspector);

                AssetList assetList = ((AssetVcsOperations.IAssetSelection)
                    mAssetSelection).GetSelectedAssets();

                if (assetList.Count == 0)
                    return;

                SelectedAssetGroupInfo selectedGroupInfo = SelectedAssetGroupInfo.
                    BuildFromAssetList(mWkInfo, assetList, mPlasticAPI, mAssetStatusCache);

                if (assetList.Count != selectedGroupInfo.SelectedCount)
                    return;

                AssetsOverlays.AssetStatus assetStatus;
                LockStatusData lockStatusData;
                GetAssetStatusToDraw(
                    assetList[0].path, assetList.Count,
                    mAssetStatusCache,
                    out assetStatus,
                    out lockStatusData);

                AssetMenuOperations assetOperations = AssetMenuUpdater.
                    GetAvailableMenuOperations(selectedGroupInfo);

                DrawAssetStatusHeader(assetStatus, lockStatusData, assetOperations);
            }
            catch (Exception ex)
            {
                ExceptionsHandler.LogException(typeof(DrawInspectorOperations).Name, ex);
            }
        }

        static void DrawAssetStatusHeader(
            AssetsOverlays.AssetStatus assetStatus,
            LockStatusData lockStatusData,
            AssetMenuOperations assetOperations)
        {
            bool guiEnabledBck = GUI.enabled;
            GUI.enabled = true;
            try
            {
                DrawBackRectangle(guiEnabledBck);

                GUILayout.BeginHorizontal();

                DrawStatusLabel(assetStatus, lockStatusData);

                DrawButtons(assetOperations);

                GUILayout.EndHorizontal();
            }
            finally
            {
                GUI.enabled = guiEnabledBck;
            }
        }

        static void DrawBackRectangle(bool isEnabled)
        {
            // when the inspector is disabled, there is a separator line
            // that breaks the visual style. Draw an empty rectangle
            // matching the background color to cover it

            GUILayout.Space(UnityConstants.INSPECTOR_ACTIONS_BACK_RECTANGLE_TOP_MARGIN);

            GUIStyle targetStyle = (isEnabled) ?
                UnityStyles.Inspector.HeaderBackgroundStyle :
                UnityStyles.Inspector.DisabledHeaderBackgroundStyle;

            Rect rect = GUILayoutUtility.GetRect(
                GUIContent.none, targetStyle);

            // extra space to cover the inspector full width
            rect.x -= 20;
            rect.width += 80;

            GUI.Box(rect, GUIContent.none, targetStyle);

            // now reset the space used by the rectangle
            GUILayout.Space(
                -UnityConstants.INSPECTOR_ACTIONS_HEADER_BACK_RECTANGLE_HEIGHT
                - UnityConstants.INSPECTOR_ACTIONS_BACK_RECTANGLE_TOP_MARGIN);
        }

        static void DrawButtons(AssetMenuOperations assetOperations)
        {
            var operationsAvailability = new Dictionary<AssetMenuOperations, bool>
            {
                { AssetMenuOperations.Add, assetOperations.HasFlag(AssetMenuOperations.Add) },
                { AssetMenuOperations.Checkout, assetOperations.HasFlag(AssetMenuOperations.Checkout) },
                { AssetMenuOperations.Checkin, assetOperations.HasFlag(AssetMenuOperations.Checkin) },
                { AssetMenuOperations.Undo, assetOperations.HasFlag(AssetMenuOperations.Undo) }
            };

            // GUILayout reserves space for controls, which might lead to unexpected layout behavior
            // when controls are hidden dynamically. We keep consistency by adding flex spaces for inactive operations.
            foreach (var unused in operationsAvailability.Values.Where(activeOperation => !activeOperation))
            {
                GUILayout.FlexibleSpace();
            }

            if (operationsAvailability[AssetMenuOperations.Add])
            {
                DoAddButton();
            }

            if (operationsAvailability[AssetMenuOperations.Checkout])
            {
                DoCheckoutButton();
            }

            if (operationsAvailability[AssetMenuOperations.Checkin])
            {
                DoCheckinButton();
            }

            if (operationsAvailability[AssetMenuOperations.Undo])
            {
                DoUndoButton();
            }
        }

        static void DrawStatusLabel(
            AssetsOverlays.AssetStatus assetStatus,
            LockStatusData lockStatusData)
        {
            Texture overlayIcon = DrawAssetOverlay.DrawOverlayIcon.
                GetOverlayIcon(assetStatus);

            if (overlayIcon == null)
                return;

            string statusText = DrawAssetOverlay.
                GetStatusString(assetStatus);

            string tooltipText = DrawAssetOverlay.GetTooltipText(
                assetStatus, lockStatusData);

            Rect selectionRect = GUILayoutUtility.GetRect(
                new GUIContent(statusText + EXTRA_SPACE, overlayIcon),
                GUIStyle.none);

            selectionRect.height = UnityConstants.OVERLAY_STATUS_ICON_SIZE;

            Rect overlayRect = OverlayRect.GetCenteredRect(selectionRect);

            GUI.DrawTexture(
                overlayRect,
                overlayIcon,
                ScaleMode.ScaleToFit);

            selectionRect.x += UnityConstants.OVERLAY_STATUS_ICON_SIZE;
            selectionRect.width -= UnityConstants.OVERLAY_STATUS_ICON_SIZE;

            GUI.Label(
                selectionRect,
                new GUIContent(statusText, tooltipText));
        }

        static void DoAddButton()
        {
            string buttonText = PlasticLocalization.GetString(PlasticLocalization.Name.AddButton);
            if (GUILayout.Button(string.Format("{0}", buttonText), EditorStyles.miniButton))
            {
                if (mOperations == null)
                    ShowWindow.Plastic();

                mOperations.Add();
            }
        }

        static void DoCheckoutButton()
        {
            string buttonText = PlasticLocalization.GetString(PlasticLocalization.Name.CheckoutButton);
            if (GUILayout.Button(string.Format("{0}", buttonText), EditorStyles.miniButton))
            {
                if (mOperations == null)
                    ShowWindow.Plastic();

                mOperations.Checkout();
            }
        }

        static void DoCheckinButton()
        {
            string buttonText = PlasticLocalization.GetString(PlasticLocalization.Name.CheckinButton);
            if (GUILayout.Button(string.Format("{0}", buttonText), EditorStyles.miniButton))
            {
                if (mOperations == null)
                    ShowWindow.Plastic();

                mOperations.Checkin();
                EditorGUIUtility.ExitGUI();
            }
        }

        static void DoUndoButton()
        {
            string buttonText = PlasticLocalization.GetString(PlasticLocalization.Name.UndoButton);
            if (GUILayout.Button(string.Format("{0}", buttonText), EditorStyles.miniButton))
            {
                if (mOperations == null)
                    ShowWindow.Plastic();

                mOperations.Undo();
                EditorGUIUtility.ExitGUI();
            }
        }

        static void GetAssetStatusToDraw(
            string selectedPath,
            int selectedCount,
            IAssetStatusCache statusCache,
            out AssetsOverlays.AssetStatus assetStatus,
            out LockStatusData lockStatusData)
        {
            assetStatus = AssetsOverlays.AssetStatus.None;
            lockStatusData = null;

            if (selectedCount > 1)
                return;

            string selectedFullPath = Path.GetFullPath(selectedPath);

            assetStatus = statusCache.GetStatus(selectedFullPath);
            lockStatusData = statusCache.GetLockStatusData(selectedFullPath);
        }

        static IAssetMenuVCSOperations mOperations;
        static InspectorAssetSelection mAssetSelection;

        static bool mIsEnabled;
        static IAssetStatusCache mAssetStatusCache;
        static IPlasticAPI mPlasticAPI;
        static WorkspaceInfo mWkInfo;

        const string EXTRA_SPACE = "    ";

        static readonly ILog mLog = PlasticApp.GetLogger("DrawInspectorOperations");
    }
}
