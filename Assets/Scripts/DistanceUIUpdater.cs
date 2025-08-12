using UnityEngine;
using TMPro;

public class DistanceUIUpdater : MonoBehaviour
{
    private TextMeshProUGUI m_DistanceText;

    void Awake()
    {
        m_DistanceText = GetComponent<TextMeshProUGUI>();
    }

    public void UpdateDistanceText(float distance)
    {
        if (m_DistanceText != null)
        {
            m_DistanceText.text = $"Distance: {distance:F2} m";
        }
    }
}