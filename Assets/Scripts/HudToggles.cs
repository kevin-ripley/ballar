// HudToggles.cs
using UnityEngine;

public class HudToggles : MonoBehaviour
{
    [Header("Feature Groups (set active/inactive)")]
    [SerializeField] private GameObject slopeGroup;
    [SerializeField] private GameObject fallLineGroup;
    [SerializeField] private GameObject contoursGroup;
    [SerializeField] private GameObject puttLineGroup;

    public void ToggleSlope(bool on)     { if (slopeGroup)     slopeGroup.SetActive(on); }
    public void ToggleFallLine(bool on)  { if (fallLineGroup)  fallLineGroup.SetActive(on); }
    public void ToggleContours(bool on)  { if (contoursGroup)  contoursGroup.SetActive(on); }
    public void TogglePuttLine(bool on)  { if (puttLineGroup)  puttLineGroup.SetActive(on); }

    // Convenience shortcuts if you want single-button toggles:
    public void FlipSlope()    { if (slopeGroup)     slopeGroup.SetActive(!slopeGroup.activeSelf); }
    public void FlipFallLine() { if (fallLineGroup)  fallLineGroup.SetActive(!fallLineGroup.activeSelf); }
    public void FlipContours() { if (contoursGroup)  contoursGroup.SetActive(!contoursGroup.activeSelf); }
    public void FlipPuttLine() { if (puttLineGroup)  puttLineGroup.SetActive(!puttLineGroup.activeSelf); }
}
