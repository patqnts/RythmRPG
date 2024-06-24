using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class PatternManagerUI : MonoBehaviour
{
    public PatternRecorder patternRecorder;
    public Dropdown patternDropdown;
    public InputField renameInputField;
    public Button deleteButton;
    public Button loadButton;
    public Button renameButton;

    void Start()
    {
        UpdatePatternDropdown();
    }

    public void UpdatePatternDropdown()
    {
        patternDropdown.ClearOptions();
        List<string> patterns = patternRecorder.LoadPatterns();
        patternDropdown.AddOptions(patterns);
    }

    public void LoadSelectedPattern()
    {
        string selectedPattern = patternDropdown.options[patternDropdown.value].text;
        patternRecorder.LoadPattern(selectedPattern);
    }

    public void DeleteSelectedPattern()
    {
        string selectedPattern = patternDropdown.options[patternDropdown.value].text;
        patternRecorder.DeletePattern(selectedPattern);
        UpdatePatternDropdown();
    }

    public void RenameSelectedPattern()
    {
        string selectedPattern = patternDropdown.options[patternDropdown.value].text;
        string newPatternName = renameInputField.text;
        if (!string.IsNullOrEmpty(newPatternName))
        {
            patternRecorder.RenamePattern(selectedPattern, newPatternName);
            UpdatePatternDropdown();
        }
    }
}
