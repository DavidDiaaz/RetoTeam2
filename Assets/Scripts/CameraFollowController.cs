using UnityEngine;

// Agrega este script a la Main Camera
// Teclas 1-9: seguir taxi N | Tecla 0 / Escape: camara libre
public class CameraFollowController : MonoBehaviour
{
    [Header("Offset respecto al taxi")]
    public Vector3 followOffset = new Vector3(0f, 6f, -10f);
    public float   followSpeed  = 5f;

    private TaxiAgent[] taxis;
    private TaxiAgent   followTarget = null;

    private Vector3    freeCamPos;
    private Quaternion freeCamRot;

    void Start()
    {
        freeCamPos = transform.position;
        freeCamRot = transform.rotation;
        Invoke(nameof(RefreshTaxiList), 0.5f);
    }

    void RefreshTaxiList()
    {
        taxis = FindObjectsByType<TaxiAgent>(FindObjectsSortMode.None);
    }

    void Update()
    {
        for (int i = 0; i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                FollowTaxi(i);
                return;
            }
        }

        if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Escape))
            ReturnToFreeCam();
    }

    void LateUpdate()
    {
        if (followTarget == null) return;
        Vector3 desired = followTarget.transform.TransformPoint(followOffset);
        transform.position = Vector3.Lerp(transform.position, desired, followSpeed * Time.deltaTime);
        transform.LookAt(followTarget.transform.position + Vector3.up * 1f);
    }

    void FollowTaxi(int index)
    {
        if (taxis == null || taxis.Length == 0) RefreshTaxiList();
        if (taxis == null || index >= taxis.Length) return;

        followTarget = taxis[index];
        Debug.Log($"[Cam] Siguiendo {followTarget.taxiId}");
    }

    void ReturnToFreeCam()
    {
        followTarget       = null;
        transform.position = freeCamPos;
        transform.rotation = freeCamRot;
    }
}
