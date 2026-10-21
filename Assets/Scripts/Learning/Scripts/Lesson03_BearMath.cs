using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Lesson03_BearMath : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        int distance = 25;
        int stepLength = 2;
        int stamina = 100;
        float time = 4;
        int timeInt = 4;

        Debug.Log("Player will take " + distance / stepLength + " steps to reach the food.");
        Debug.Log("Player will take " + distance % stepLength + " steps to reach the food.");
   
        Debug.Log ("Bear has " + stamina + " stamina points.");
        
        Debug.Log ("After the first jerkBear will have " + (stamina -= 15) + " stamina points after running.");
        Debug.Log ("After the second jerkBear will have " + (stamina -= 15) + " stamina points after running.");
        Debug.Log ("After the third jerkBear will have " + (stamina -= 15) + " stamina points after running.");
        Debug.Log ("Speed of the bear is " + distance / time + " meters per second.");
        Debug.Log ("Speed of the bear is " + distance / timeInt + " meters per second.");
        
    
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
