using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GridDivider : MonoBehaviour
{
    public int numberOfColumns = 5; // Set the number of columns as per your requirement
    public GameObject squarePrefab; // Drag and drop your square sprite prefab in the inspector

    void Start()
    {
        GenerateColumns();
    }

    void GenerateColumns()
    {
        float columnWidth = 1.0f / numberOfColumns;

        for (int i = 0; i < numberOfColumns; i++)
        {
            float columnPosition = i * columnWidth + columnWidth / 2.0f;

            GameObject square = Instantiate(squarePrefab, new Vector3(columnPosition, 0, 0), Quaternion.identity);
            square.transform.SetParent(transform);
            square.transform.localScale = new Vector3(columnWidth, 1, 1);
        }
    }
}
