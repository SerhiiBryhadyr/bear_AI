using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Lesson02_BearStats : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        string bearName = "Shatun";
        int bearAge = 5;
        float bearSpeed = 6f;
        bool isBearHungry = false;

        Debug.Log("Bear Name: " + bearName);
        Debug.Log("Bear Age: " + bearAge);
        Debug.Log("Bear Speed: " + bearSpeed);
        Debug.Log("Is Bear Hungry: " + isBearHungry);

        bearSpeed = 9f;         
        isBearHungry = true;
        Debug.Log("Bear Speed: " + bearSpeed);
        Debug.Log("Is Bear Hungry: " + isBearHungry);
    }

    // Update is called once per frame komment line

}
