// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Globalization;
using Avalonia.Data.Converters;

namespace Files.App.Linux.Converters;

/// <summary>True when the bound value equals the converter parameter (compared as strings).</summary>
public sealed class EqualsConverter : IValueConverter
{
	public static readonly EqualsConverter Instance = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> string.Equals(System.Convert.ToString(value, CultureInfo.InvariantCulture), System.Convert.ToString(parameter, CultureInfo.InvariantCulture), StringComparison.Ordinal);

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
