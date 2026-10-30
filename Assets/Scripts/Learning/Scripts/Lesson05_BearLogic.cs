using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Lesson05_BearLogic : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        float distance = 100f;
        bool seesPlayer = true; bool playerRuns = false; bool playerCrouches = true;
        GameObject flashlight = null;

        if (seesPlayer == true && playerRuns == true)
        {
            Debug.Log ("Chase");
        }
        else if (seesPlayer == true || distance < 5f)
        {
            Debug.Log("Bear knows where you are");
        }
        else
        {
            Debug.Log("Bear does not know");
        }

        if (playerCrouches == !true && distance < 20f)
        {
            Debug.Log ("Bear hears footsteps");
        } 
if (flashlight !=null)
        {
            Debug.Log ("Flashlight is here");
        } 
        else {
            Debug.Log ("No flashlight");
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
