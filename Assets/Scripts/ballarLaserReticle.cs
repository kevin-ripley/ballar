// ballarLaserReticle.cs
using UnityEngine;

public class ballarLaserReticle : MonoBehaviour
{
    [Header("System References")]
    [SerializeField] private ballarLaser m_Laser;
    [SerializeField] private GameObject m_ReticleVisual; // assign a flat quad/circle
    [SerializeField] private float m_ReticleOffset = 0.002f; // lift slightly off surface

    void Awake()
    {
        if (!m_ReticleVisual && transform.childCount > 0)
            m_ReticleVisual = transform.GetChild(0).gameObject;
    }

    void LateUpdate()
    {
        if (!m_Laser || !m_ReticleVisual) return;

        if (m_Laser.IsHitting)
        {
            m_ReticleVisual.SetActive(true);
            var p = m_Laser.CurrentHit.point + m_Laser.CurrentHit.normal * m_ReticleOffset;
            m_ReticleVisual.transform.position = p;
            // Align to surface normal, not just face camera
            m_ReticleVisual.transform.rotation = Quaternion.LookRotation(m_Laser.CurrentHit.normal);
        }
        else
        {
            m_ReticleVisual.SetActive(false);
        }
    }
}
