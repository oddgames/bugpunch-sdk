using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ODDGames.UIAutomation
{
    [Serializable]
    public class UITestRecordingData
    {
        public string testName;
        public string description;
        public string recordedAt;
        public float totalDuration;
        public string deviceInfo;
        public string unityVersion;
        public string testDataSourcePath;
        public List<UITestRecordingStep> steps = new();

        public UITestRecordingData()
        {
            recordedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            deviceInfo = $"{SystemInfo.deviceModel} ({SystemInfo.operatingSystem})";
            unityVersion = Application.unityVersion;
        }

        public string ToStepsLog()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Recording: {testName}");
            sb.AppendLine($"# Date: {recordedAt}");
            sb.AppendLine($"# Device: {deviceInfo}");
            sb.AppendLine($"# Unity: {unityVersion}");
            sb.AppendLine($"# Duration: {totalDuration:F2}s");
            sb.AppendLine();

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                sb.AppendLine($"Step {i + 1} [{step.timestamp:F2}s] {step.type}");

                if (step.timeSinceLastStep > 0)
                {
                    sb.AppendLine($"  Gap: {step.timeSinceLastStep:F2}s (avg {step.avgFpsSinceLastStep:F0} fps)");
                    if (step.timeSinceLastStep >= 2f && step.avgFpsSinceLastStep >= 30f)
                        sb.AppendLine($"  ** DELIBERATE WAIT DETECTED **");
                }

                if (!string.IsNullOrEmpty(step.targetName))
                    sb.AppendLine($"  Target: {step.targetName}");
                if (!string.IsNullOrEmpty(step.textContent))
                    sb.AppendLine($"  Text: {step.textContent}");
                if (!string.IsNullOrEmpty(step.targetPath))
                    sb.AppendLine($"  Path: {step.targetPath}");
                if (!string.IsNullOrEmpty(step.targetType))
                    sb.AppendLine($"  Type: {step.targetType}");
                if (!string.IsNullOrEmpty(step.parentName))
                    sb.AppendLine($"  Parent: {step.parentName}");
                if (step.siblingCount > 1)
                    sb.AppendLine($"  Sibling: {step.siblingIndex + 1} of {step.siblingCount} (index={step.siblingIndex})");
                if (!string.IsNullOrEmpty(step.grandparentName))
                    sb.AppendLine($"  Grandparent: {step.grandparentName}");
                if (step.type == UITestRecordingStep.StepType.Hold)
                    sb.AppendLine($"  Duration: {step.duration:F2}s");
                if (step.type == UITestRecordingStep.StepType.Drag || step.type == UITestRecordingStep.StepType.Scroll)
                {
                    sb.AppendLine($"  DragStart: ({step.dragStartPosition.x:F0}, {step.dragStartPosition.y:F0})");
                    sb.AppendLine($"  DragEnd: ({step.dragEndPosition.x:F0}, {step.dragEndPosition.y:F0})");
                    sb.AppendLine($"  DragDelta: ({step.dragDelta.x:F0}, {step.dragDelta.y:F0})");
                    sb.AppendLine($"  Duration: {step.duration:F2}s");
                }
                if (!string.IsNullOrEmpty(step.inputText))
                    sb.AppendLine($"  Input: {step.inputText}");
                if (!string.IsNullOrEmpty(step.additionalContext))
                    sb.AppendLine($"  Note: {step.additionalContext}");
                if (!string.IsNullOrEmpty(step.keyCode))
                    sb.AppendLine($"  Key: {step.keyCode}");
                if (!string.IsNullOrEmpty(step.previousScene))
                    sb.AppendLine($"  From: {step.previousScene}");
                if (!string.IsNullOrEmpty(step.newScene))
                    sb.AppendLine($"  To: {step.newScene}");
                if (!string.IsNullOrEmpty(step.screenshotPath))
                    sb.AppendLine($"  Screenshot: {step.screenshotPath}");

                sb.AppendLine();
            }

            return sb.ToString();
        }
    }

    [Serializable]
    public class UITestRecordingStep
    {
        public StepType type;
        public float timestamp;
        public float duration;
        public float avgFpsSinceLastStep;
        public float timeSinceLastStep;
        public string targetName;
        public string targetPath;
        public string targetType;
        public string parentName;
        public Vector2 screenPosition;
        public string textContent;
        public string inputText;
        public string additionalContext;
        public string screenshotPath;

        public int siblingIndex;
        public int siblingCount;
        public string grandparentName;

        public Vector2 dragStartPosition;
        public Vector2 dragEndPosition;
        public Vector2 dragDelta;

        public enum StepType
        {
            Click,
            Hold,
            TextInput,
            Wait,
            Note,
            SceneChange,
            Drag,
            Scroll,
            KeyPress
        }

        // For KeyPress steps
        public string keyCode;

        public string previousScene;
        public string newScene;
    }
}
