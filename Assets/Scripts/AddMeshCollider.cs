using UnityEngine;
using UnityEngine.XR.ARFoundation; // You need this namespace

[RequireComponent(typeof(ARMeshManager))]
public class AddMeshCollider : MonoBehaviour
{
    private ARMeshManager meshManager;

    void Awake()
    {
        meshManager = GetComponent<ARMeshManager>();
    }

    void OnEnable()
    {
        meshManager.meshesChanged += OnMeshesChanged;
    }

    void OnDisable()
    {
        meshManager.meshesChanged -= OnMeshesChanged;
    }

    // This function is called when AR Foundation adds, updates, or removes meshes.
    void OnMeshesChanged(ARMeshesChangedEventArgs args)
    {
        // Add a collider to any new mesh that is generated.
        foreach (var mesh in args.added)
        {
            if (mesh.GetComponent<MeshCollider>() == null)
            {
                mesh.gameObject.AddComponent<MeshCollider>();
            }
        }

        // Also ensure updated meshes have one.
        foreach (var mesh in args.updated)
        {
            if (mesh.GetComponent<MeshCollider>() == null)
            {
                mesh.gameObject.AddComponent<MeshCollider>();
            }
        }
    }
}