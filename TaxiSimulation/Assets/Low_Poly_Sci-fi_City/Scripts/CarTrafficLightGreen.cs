using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CarTrafficLightGreen : MonoBehaviour
{
    [SerializeField] private Material[] trafficMaterials;
    Renderer rendererMat;

    [SerializeField] private float redTime = 10f;

    [SerializeField] private float greenTime = 10f;

    private void Start() 
    {
        rendererMat = GetComponent<Renderer>();
        rendererMat.enabled = true;
        //rendererMat.sharedMaterial = trafficMaterials[2];

        StartCoroutine(SwitchLights());
    }

    IEnumerator SwitchLights()
    {
        while (true)
        {
            rendererMat.sharedMaterial = trafficMaterials[2];
            yield return new WaitForSeconds(greenTime);

            rendererMat.sharedMaterial = trafficMaterials[1];
            yield return new WaitForSeconds(2);

            rendererMat.sharedMaterial = trafficMaterials[0];
            yield return new WaitForSeconds(redTime);

            rendererMat.sharedMaterial = trafficMaterials[1];
            yield return new WaitForSeconds(2);
        }
    }
}
