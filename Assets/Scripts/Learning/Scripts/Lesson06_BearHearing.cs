using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Lesson06_BearHearing : MonoBehaviour
{
    enum PlayerMove {Crouch, Walk, Run}
    int move = (int)PlayerMove.Run;
    float hearRadius = 0f;
    // Start is called before the first frame update
    void Start()
    {
        switch (move)
        {
            case (int)PlayerMove.Crouch:
                hearRadius = 2.5f;
                break;
            case (int)PlayerMove.Walk:
                hearRadius = 10f;
                break;
            case (int)PlayerMove.Run:
                hearRadius = 20f;
                break;
        }
        Debug.Log("Ведмідь чує з " + hearRadius + " м");
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
