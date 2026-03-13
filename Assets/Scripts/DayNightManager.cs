using UnityEngine;

public class DayNightManager : MonoBehaviour
{
    public float rotationSpeed = 10f;

    void Update()
    {
        transform.Rotate(-rotationSpeed * Time.deltaTime, 0f, 0f);
    }
}