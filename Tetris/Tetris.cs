using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.IO;

namespace Tetris
{
    public class Tetris
    {
        // We'll use bits 11-2 of the 16 bits of the unsigned shorts in an array to represent the positions of the blocks. E.g. xxxx0000000000xx (x = don't care)    
        // The ushort array starts at zero, representing the bottom of the tetris grid.
        // The applications uses bitwise operation to check for clashes (AND) and populate the array (OR).

        #region Private Variables

        static Dictionary<string, ushort[]> shapes;
        static int maxHeight;

        #endregion 

        #region Public Static Methods

        /// <summary>
        /// Accept an input file and generate an output file containing the height
        /// </summary>
        /// <param name="inputFile"></param>
        /// <param name="outputFile"></param>
        /// <returns></returns>
        public static void ProcessFile(string inputFile, string outputFile)
        {
            // Dictionary to store the shape definitions
            shapes = GetShapes();

            // Set the max height of the grid from the config file
            maxHeight = int.Parse(ConfigurationManager.AppSettings["maxHeight"]);

            // Read the input as a list of sets of commands
            var commands = GetInputCommands(inputFile);

            // Process the command sets
            var results = ProcessCommands(commands);

            // Output the results
            OutputResults(results, outputFile);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Retrieve all shapes from the config file
        /// </summary>
        /// <returns>A dictionary containing the shape definitions</returns>
        static Dictionary<string, ushort[]> GetShapes()
        {
            Dictionary<string, ushort[]> d = new Dictionary<string, ushort[]>();

            // Get the shape definitions from the config
            var shapeKeys = ConfigurationManager.AppSettings.AllKeys.Where(k => k.StartsWith("shape"));
            foreach (var key in shapeKeys)
            {
                var letter = key.Substring(key.Length - 1, 1);
                var shapeDefinition = ConfigurationManager.AppSettings[key].Split(',');
                d.Add(letter, Array.ConvertAll(shapeDefinition, ushort.Parse));
            }

            return d;
        }

        /// <summary>
        /// Read the input file and return the set of input commands
        /// </summary>
        /// <param name="inputFile"></param>
        /// <returns>A list of a list of InputCommand objects</returns>
        static List<List<InputCommand>> GetInputCommands(string inputFile)
        {
            List<List<InputCommand>> list = new List<List<InputCommand>>();

            // Open the file
            var reader = File.OpenText(inputFile);

            // Read until the end of the file
            while (!reader.EndOfStream)
            {
                string line = reader.ReadLine();
                if (line != "")
                {
                    List<InputCommand> commands = new List<InputCommand>();
                    var split = line.Split(',');
                    
                    foreach (var command in split)
                        commands.Add(new InputCommand(command.Substring(0, 1), int.Parse(command.Substring(1, 1))));

                    list.Add(commands);
                }
            }

            reader.Close();

            return list;
        }

        /// <summary>
        /// Processes the input commands and returns a list of heights, as per the specification
        /// </summary>
        /// <param name="commands"></param>
        /// <returns></returns>
        static List<int> ProcessCommands(List<List<InputCommand>> commands)
        {
            List<int> results = new List<int>();

            // Go through each set of one or more commands
            foreach (var commandSet in commands)
            {
                // A grid to store the shape positions (0 is the bottom of the grid). 
                // Give a bit of extra leeway in the array above the specified amount to account for the edge case of a stack going all the way up to the limit.
                ushort[] grid = new ushort[maxHeight + 3];

                // A variable to store the highest entry
                int highest = 0;

                // For each command in the set, determine where it should end up in the grid
                foreach (var command in commandSet)
                {
                    // Grab the shape definition
                    var shape = shapes[command.Letter];

                    // The strategy here is to left shift the shape to the correct bits, then use a bitwise AND. The result of the operation should be zero if there is no clash.
                    // We can't start at the bottom and work our way up, as the shape might drop through another shape into a gap beneath. Start at 3 above the last highest entry and work down.
                    // Once we've found a place for the block to go, we can bitwise OR the shifted value in there and check for filled rows.
                    
                    int yPosFound = 2;

                    // Check each row of the shape against each row in the grid starting 3 rows above the highest entry so far of the grid and working our way down
                    for (int yOrigin = highest + 2; yOrigin >= 2; yOrigin--)
                    {
                        bool clash = false;

                        // Check the whole shape
                        for (int y = 0; y < 3; y++)
                        {
                            var shapeRow = shape[y];
                            var shifted = shapeRow << 8 - command.Position; // Left shift by 8 minus the position in the grid
                            var andResult = shifted & grid[yOrigin - y]; // Bitwise AND the shifted block against the grid values
                            if (andResult != 0)
                            {
                                // If the result of the bitwise AND is non-zero then we've got a clash at this y position
                                clash = true;
                                break;
                            }
                        }

                        // If we've encountered a clash then set the yOrigin to the previous one and break
                        if (clash)
                        {
                            yPosFound = yOrigin + 1;
                            break;
                        }
                    }

                    // Now we've found where it can go, OR the values in (using a mask to ensure we don't include any weird stray bits outside the range of bits we're interested in).
                    // Also not forgetting to left shift the shape to the appropriate place (8 - the position).
                    int yy = 0;
                    for (int y = yPosFound; y >= yPosFound - 2; y--)
                        grid[y] = (ushort)(grid[y] | (shape[yy++] << 8 - command.Position) & 0b111111111100);

                    // Find the highest populated row
                    for (int y = grid.Length - 1; y >= 0; y--)
                    {
                        if (grid[y] != 0)
                        {
                            highest = y;
                            break;
                        }
                    }

                    // Check the active area of the grid for filled rows
                    for (int y = highest; y >= 0; y--)
                    {
                        if (grid[y] == 0b111111111100)
                        {
                            Array.Copy(grid, y + 1, grid, y, highest);
                            grid[highest] = 0;
                        }
                    }
                }

                highest = 0;
                bool zeroPopulated = true;

                // Find the highest populated row
                for (int y = grid.Length - 1; y >= 0; y--)
                {
                    if (grid[y] != 0)
                    {
                        highest = y;
                        zeroPopulated = false;
                        break;
                    }
                }
                // Add the highest row filled to the results (+ 1 to account for the zero origin array)
                results.Add(zeroPopulated ? 0 : highest + 1);
            }

            return results;
        }

        /// <summary>
        /// Write an integer list to an output file
        /// </summary>
        /// <param name="results"></param>
        /// <param name="outputFile"></param>
        static void OutputResults(List<int> results, string outputFile)
        {
            File.WriteAllLines(outputFile, results.Select(r => r.ToString()));
        }

        #endregion

        #region Private Classes

        /// <summary>
        /// Simple class to store input commands
        /// </summary>
        class InputCommand
        {
            public string Letter { get; set; }
            public int Position { get; set; }

            public InputCommand(string letter, int position)
            {
                Letter = letter;
                Position = position;
            }
        }

        #endregion
    }
}
