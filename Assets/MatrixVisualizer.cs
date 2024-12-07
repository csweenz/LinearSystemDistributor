using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class MatrixVisualizer : NetworkBehaviour
{
    public GameObject matrixCellPrefab;
    public Transform matrixDisplay;
    public Transform augmentedDisplay; 
    public InputField inputField;
    public Button confirmButton;
    public Button randomizeCoefficientsButton;
    public Button randomizeSolnButton;
    public Button newTurnButton;
    public GameObject solutionVectorDisplay;

    private GameObject[,] matrixCells;
    private GameObject[] augmentedCells;

    public enum GameState { SetRows, SetColumns, SetCoefficients, SetSolution, ConfirmSetup, StartGame, Connecting, ViewingMatrix, AdjustingSliders, GuessConfirmed }

    public GameState currentState;
    
    public NetworkVariable<int> totalRows = new NetworkVariable<int>();
    public NetworkVariable<int> totalColumns = new NetworkVariable<int>();
    public NetworkList<float> coefficientList = new NetworkList<float>();
    private NetworkList<float> solutionVector = new NetworkList<float>(); 
    public NetworkList<float> augmentedVectorB = new NetworkList<float>(); 


    public override void OnNetworkSpawn()
    {
        totalRows.OnValueChanged += OnTotalRowsChanged;
        totalColumns.OnValueChanged += OnTotalColumnsChanged;
        solutionVector.OnListChanged += OnSolutionVectorChanged;
        augmentedVectorB.OnListChanged += OnAugmentedVectorBChanged;
        coefficientList.OnListChanged += OnCoefficientListChanged;

        if (IsClient && !IsServer)
        {
            DisableServerViewControls();
        }
    }

    public override void OnNetworkDespawn()
    {
        // Unsubscribe from OnListChanged events to prevent memory leaks.  ***Still getting warning***
        solutionVector.OnListChanged -= OnSolutionVectorChanged;
        augmentedVectorB.OnListChanged -= OnAugmentedVectorBChanged;
        coefficientList.OnListChanged -= OnCoefficientListChanged;
    }

    // Event handlers for NetworkVariable changes
    private void OnTotalRowsChanged(int previousValue, int newValue)
    {
        // Update UI based on new totalRows value (if needed)
    }

    private void OnTotalColumnsChanged(int previousValue, int newValue)
    {
        // Update UI based on new totalColumns value (if needed)
    }

    public void SetTotalRows(int rows)
    {
        if (IsServer)
        {
            totalRows.Value = rows;
        }
    }

    public void SetTotalColumns(int columns)
    {
        if (IsServer)
        {
            totalColumns.Value = columns;
            InitializeCoefficientList(totalRows.Value, columns);
        }
    }

    private void InitializeCoefficientList(int rows, int columns)
    {
        if (totalRows.Value > 0 && totalColumns.Value > 0)
        {
            
            int totalElements = rows * columns;
            for (int i = 0; i < totalElements; i++)
            {
                coefficientList.Add(0f); // Initialize with 0s
            }
        }
    }

    public void SetCoefficient(int rowIndex, int columnIndex, float value)
    {
        if (IsServer)
        {
            int index = rowIndex * totalColumns.Value + columnIndex;
            if (index >= 0 && index < coefficientList.Count)
            {
                coefficientList[index] = value;
            }
        }
    }

    public void SetSolutionVector(float[] solution)
    {
        if (IsServer)
        {
            solutionVector.Clear(); 
            foreach (float value in solution)
            {
                solutionVector.Add(value);
            }
        }
    }

    public void SetAugmentedVectorB(float[] resultVectorB)
    {
        if (IsServer)
        {
            augmentedVectorB.Clear(); 
            foreach (float value in resultVectorB)
            {
                augmentedVectorB.Add(value);
            }
        }
    }

    public void GenerateMatrix(int rows, int columns)
    {
        // Store dimensions in network variables for spawning appropriate controls later
        SetTotalRows(rows);
        SetTotalColumns(columns);
        Debug.Log($"Generating Matrix...");

        // Instantiate new cells
        ClearMatrix();
        matrixCells = new GameObject[rows, columns];

        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                Debug.Log($"Instantiating cell at ({i}, {j})");
                GameObject cell = Instantiate(matrixCellPrefab, matrixDisplay.transform);
                InputField cellInput = cell.GetComponentInChildren<InputField>();
                RectTransform inputSize = cellInput.GetComponent<RectTransform>();
                inputSize.sizeDelta = new Vector2(360/rows, 240/columns);
                RectTransform cellSize = cell.GetComponent<RectTransform>();
                cellSize.sizeDelta = new Vector2(360/rows, 240/columns);
                cellInput.text = $"A({i},{j})"; // Initial placeholder
                matrixCells[i, j] = cell;
            }
        }

        Debug.Log($"Generated matrix: {rows} rows x {columns} columns");
    }

    public void SetupGridLayout(int rows, int columns)
    {
        GridLayoutGroup gridLayout = matrixDisplay.GetComponent<GridLayoutGroup>();
        if (gridLayout != null)
        {
            gridLayout.cellSize = new Vector2(360/rows, 240/columns); // Size of each prefab
            gridLayout.spacing = new Vector2(5, 5); // Spacing between prefabs
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = columns;

            /*/ Fit all cells comfortably - This resizes the entire display window for really large matrices, but moves it unnecessarily for small ones
            RectTransform rectTransform = matrixDisplay.GetComponent<RectTransform>();
            float totalWidth = columns * 40 + (columns - 1) * 5;
            float totalHeight = rows * 40 + (rows - 1) * 5;
            rectTransform.sizeDelta = new Vector2(totalWidth, totalHeight);*/  
        }
        GridLayoutGroup gridBLayout = augmentedDisplay.GetComponent<GridLayoutGroup>();
        if (gridLayout != null)
        {
            gridBLayout.cellSize = new Vector2(360/rows, 60); // Size of each prefab
            gridBLayout.spacing = new Vector2(5, 5); // Spacing between prefabs
            gridBLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridBLayout.constraintCount = 1;

            /*/ Fit all cells comfortably - This resizes the entire display window for really large matrices, but moves it unnecessarily for small ones
            RectTransform rectTransform = matrixDisplay.GetComponent<RectTransform>();
            float totalWidth = columns * 40 + (columns - 1) * 5;
            float totalHeight = rows * 40 + (rows - 1) * 5;
            rectTransform.sizeDelta = new Vector2(totalWidth, totalHeight);*/  
        }
    }

    public void UpdateMatrixData(int rowIndex, int columnIndex, float value)
    {
        if (IsServer)
        {
            // Update the coefficient at row and column
            SetCoefficient(rowIndex, columnIndex, value);

            // Update matrix cells visually
            UpdateCellValue(rowIndex, columnIndex, value);
        }
    }

    public void UpdateAugmentedMatrix(NetworkList<float> resultVectorB)
    {
        
        {
            // Update display
            ClearAugmentedCells();
            augmentedCells = new GameObject[resultVectorB.Count];
            for (int i = 0; i < resultVectorB.Count; i++)
            {
                GameObject cell = Instantiate(matrixCellPrefab, augmentedDisplay.transform);
                InputField cellInput = cell.GetComponentInChildren<InputField>();
                RectTransform inputSize = cellInput.GetComponent<RectTransform>();
                inputSize.sizeDelta = new Vector2(360/resultVectorB.Count, 60);
                RectTransform cellSize = cell.GetComponent<RectTransform>();
                cellSize.sizeDelta = new Vector2(360/resultVectorB.Count, 60);
                cellInput.text = resultVectorB[i].ToString("F2");
                cellInput.interactable = false;
                augmentedCells[i] = cell;
            }

            Debug.Log("Augmented vector (b) updated and displayed.");
        }
    }

    private void OnSolutionVectorChanged(NetworkListEvent<float> changeEvent)
    {
        // Access the updated solutionVector using solutionVector list
    }

    private void OnAugmentedVectorBChanged(NetworkListEvent<float> changeEvent)
    {
        UpdateAugmentedMatrix(augmentedVectorB); // Pass the updated list
    }

    private void OnCoefficientListChanged(NetworkListEvent<float> changeEvent)
    {
        // Reflect changes in the coefficientList
        int rowIndex = changeEvent.Index / totalColumns.Value;
        int columnIndex = changeEvent.Index % totalColumns.Value;
        UpdateCellValue(rowIndex, columnIndex, coefficientList[changeEvent.Index]);
    }

    public float GetCoefficient(int rowIndex, int columnIndex)
    {
        int index = rowIndex * totalColumns.Value + columnIndex;
        if (index >= 0 && index < coefficientList.Count)
        {
            return coefficientList[index];
        }
        return 0f; // Default value if indices are out of range
    }

    private void ClearMatrix()
    {
        foreach (Transform child in matrixDisplay.transform)
        {
            Destroy(child.gameObject);
        }
    }

    private void ClearAugmentedCells()
    {
        foreach (Transform child in augmentedDisplay.transform)
        {
            Destroy(child.gameObject);
        }
    }

    public void UpdateCellValue(int row, int column, float value)
    {
        if (matrixCells != null && row < matrixCells.GetLength(0) && column < matrixCells.GetLength(1))
        {
            InputField cellInput = matrixCells[row, column].GetComponentInChildren<InputField>();
            cellInput.text = value.ToString("F2");
        }
    }

    public void DisplayClientView()
    {
    DisableServerViewControls();
    EnableClientViewControls();
    }

    public void DisableServerViewControls()
    {
        if (!IsClient || IsServer)
        {
        Debug.Log("Server controls disabled by MatrixVisualizer.");
        return;
        }
        // Disable controls that clients should not have access to
        inputField.gameObject.SetActive(false);
        confirmButton.gameObject.SetActive(false);
        randomizeCoefficientsButton.gameObject.SetActive(false);
        randomizeSolnButton.gameObject.SetActive(false);
        
        if (solutionVectorDisplay != null)
        {
            solutionVectorDisplay.SetActive(false);
        }
    }

    private void EnableClientViewControls()
    {
        if (matrixDisplay != null) matrixDisplay.gameObject.SetActive(true);
        if (augmentedDisplay != null) augmentedDisplay.gameObject.SetActive(true);
    }
}
