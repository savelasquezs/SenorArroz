using System.Text;
using System.Text.Json;

namespace SenorArroz.API.Extensions;

/// <summary>
/// Custom naming policy for snake_case enum serialization
/// Converts PascalCase to snake_case (e.g., InPreparation -> in_preparation)
/// </summary>
public class SnakeCaseNamingPolicy : JsonNamingPolicy
{
    public override string ConvertName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var result = new StringBuilder();
        result.Append(char.ToLower(name[0]));

        for (int i = 1; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]))
            {
                result.Append('_');
                result.Append(char.ToLower(name[i]));
            }
            else
            {
                result.Append(name[i]);
            }
        }

        return result.ToString();
    }
}

