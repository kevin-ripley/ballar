using UnityEngine;
using TMPro;

public class CameraDistanceDisplay : MonoBehaviour
{
    private TextMeshProUGUI m_DistanceText;
    private const float metersToFeet = 3.28084f;

    void Awake()
    {
        m_DistanceText = GetComponent<TextMeshProUGUI>();
        if(m_DistanceText != null) m_DistanceText.text = "Distance: --";
    }

    // This public function will be called by the laser's event
    public void UpdateDistance(float distanceInMeters)
    {
        float distanceInFeet = distanceInMeters * metersToFeet;
        m_DistanceText.text = $"Distance: {distanceInFeet:F1} ft";
    }
}