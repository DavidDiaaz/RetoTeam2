using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PeopleTrafficLightGreen : MonoBehaviour
{
    [SerializeField] private Material[] trafficMaterials;
    Renderer rendererMat;

    [SerializeField] private float redTime = 12f;

    [SerializeField] private float greenTime = 12f;

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
            rendererMat.sharedMaterial = trafficMaterials[1];
            yield return new WaitForSeconds(greenTime);

            rendererMat.sharedMaterial = trafficMaterials[0];
            yield return new WaitForSeconds(redTime);
        }
    }
}
