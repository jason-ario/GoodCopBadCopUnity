using System.Linq;
using HighlightPlus;
using TMPro;
using UnityEngine;

public class ApplicationLetter : FolderItem
{
    [SerializeField] private TextMeshPro nameText;
    [SerializeField] private TextMeshPro birthDateText;
    [SerializeField] private TextMeshPro sexText;
    [SerializeField] private TextMeshPro reasonForEntryText;
    [SerializeField] private TextMeshPro idNumberText;

    public void SetInfo(SuspectCharacter suspectCharacter)
    {
        SuspectData suspectData = suspectCharacter.Data;
        nameText.text = suspectData.FirstName + " " + suspectData.LastName;
        birthDateText.text = suspectData.DateOfBirth;
        sexText.text = suspectData.Sex;
        idNumberText.text = suspectData.IDNumber;
        
        int dayNo = ShiftManager.Instance.CurrentDay;
        string[] possibleReasons;
        
        if (dayNo < 11)
        {
            possibleReasons = suspectData.entryReasons.earlyDaysReasons;
        } else if (dayNo < 21)
        {
            possibleReasons = suspectData.entryReasons.midDaysReasons;
        }
        else
        {
            possibleReasons = suspectData.entryReasons.finalDaysReasons;
        }

        int chosenReason = suspectCharacter.ChosenEntryReasonIndex;
        reasonForEntryText.text = possibleReasons[chosenReason];
        
        CheckAnomalies(suspectCharacter);
        
        //Change fonts
        nameText.font = suspectData.handwritingFont;
        reasonForEntryText.font = suspectData.handwritingFont;
        birthDateText.font = suspectData.handwritingFont;
        sexText.font = suspectData.handwritingFont;
    }

    void CheckAnomalies(SuspectCharacter suspectCharacter)
    {
        if (suspectCharacter.AnomalyController.activeAnomalies.OfType<NameWrong>().Any())
        {
            SetNameWrong();
        }
        
        if (suspectCharacter.AnomalyController.activeAnomalies.OfType<BirthDateWrong>().Any())
        {
            SetBirthDateWrong();
        }
        
        if (suspectCharacter.AnomalyController.activeAnomalies.OfType<InvalidEntryReason>().Any())
        {
            SetInvalidEntryReason(suspectCharacter.Data);
        }
        
        if (suspectCharacter.AnomalyController.activeAnomalies.OfType<IDNumberWrong>().Any())
        {
            SetIDNumberWrong(suspectCharacter);
        }
    }

    private void SetIDNumberWrong(SuspectCharacter suspectCharacter)
    {
        string originalIDNumber = suspectCharacter.Data.IDNumber; // Assuming this property exists
    
        if (string.IsNullOrEmpty(originalIDNumber) || originalIDNumber.Length != 7)
        {
            Debug.LogWarning("Invalid ID number format");
            return;
        }
    
        // Convert to char array to modify
        char[] idChars = originalIDNumber.ToCharArray();
    
        // Randomly select a position to change (0-6)
        int positionToChange = UnityEngine.Random.Range(0, 7);
    
        // Generate a different digit (0-9) that's not the current one
        int currentDigit = int.Parse(idChars[positionToChange].ToString());
        int newDigit = UnityEngine.Random.Range(0, 10);
    
        // Keep generating until we get a different digit
        while (newDigit == currentDigit)
        {
            newDigit = UnityEngine.Random.Range(0, 10);
        }
    
        idChars[positionToChange] = newDigit.ToString()[0];
    
        string wrongIDNumber = new string(idChars);
    
        // Set the wrong ID number (you may need to adjust this based on your data structure)
        idNumberText.text = wrongIDNumber;
    }

    private void SetInvalidEntryReason(SuspectData suspectData)
    {
        // Randomly decide: either use an invalid reason or leave it blank
        if (UnityEngine.Random.value > 0.5f)
        {
            // Leave it blank
            reasonForEntryText.text = "";
        }
        else
        {
            int dayNo = ShiftManager.Instance.CurrentDay;
            string[] possibleReasons;
            
            if (dayNo < 11)
            {
                possibleReasons = suspectData.invalidEntryReasons.earlyDaysReasons;
            } else if (dayNo < 21)
            {
                possibleReasons = suspectData.invalidEntryReasons.midDaysReasons;
            }
            else
            {
                possibleReasons = suspectData.invalidEntryReasons.finalDaysReasons;
            }

            reasonForEntryText.text = possibleReasons[UnityEngine.Random.Range(0, possibleReasons.Length)];
        }
    }
    
    private void SetNameWrong()
    {
        string currentName = nameText.text;
        char[] nameChars = currentName.ToCharArray();
    
        // Determine how many characters to mess up (1-3 random characters)
        int charactersToMessUp = UnityEngine.Random.Range(1, Mathf.Min(4, nameChars.Length));
    
        for (int i = 0; i < charactersToMessUp; i++)
        {
            // Pick a random character position (excluding spaces)
            int randomIndex;
            do
            {
                randomIndex = UnityEngine.Random.Range(0, nameChars.Length);
            } while (nameChars[randomIndex] == ' ');
        
            // Replace with a random letter
            nameChars[randomIndex] = (char)UnityEngine.Random.Range('a', 'z' + 1);
        }
    
        nameText.text = new string(nameChars);
    }

    private void SetBirthDateWrong()
    {
        string currentDate = birthDateText.text;
        char[] dateChars = currentDate.ToCharArray();
    
        // Randomly pick 1 or 2 characters to mess up
        int charactersToMessUp = UnityEngine.Random.Range(1, 3);
    
        for (int i = 0; i < charactersToMessUp; i++)
        {
            // Pick a random digit position (only mess with numbers, not slashes)
            int randomIndex;
            do
            {
                randomIndex = UnityEngine.Random.Range(0, dateChars.Length);
            } while (dateChars[randomIndex] == '/');
        
            // Replace with a random digit
            dateChars[randomIndex] = (char)UnityEngine.Random.Range('0', '9' + 1);
        }
    
        birthDateText.text = new string(dateChars);
    }
    
    public void SetInsideFolder(FolderController folder)
    {
        insideThisFolder = folder;
    }
    
    public override void OnEquipped(PlayerPickupController player)
    {
        base.OnEquipped(player);
      
        if (insideThisFolder)
        {
            insideThisFolder.RemoveDocument(this, player);
        }
    }
}
