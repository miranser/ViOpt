
using System;

public class Progress
{
    int Percentage;
    public Progress()
    {

    }
    public void Update(int percentage)
    {
        Console.SetCursorPosition(0, Console.CursorTop);
        PrintBar(percentage);
    }
    public void StartNew()
    {
        Console.WriteLine();
    }
    private void PrintBar(int percentage)
    {
        Console.Write("[");
        for (int i = 0; i < 10; i++)
        {
            if ((i + 1) <= percentage / 10)
                Console.Write("=");
            else Console.Write("-");
        }
        Console.Write($"] {percentage}%");

    }

}