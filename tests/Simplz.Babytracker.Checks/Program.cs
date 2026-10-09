using System.Globalization;
using Simplz.Babytracker.Checks;

// Printed output and anything compared through ToString read the same on every machine.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var h = await Harness.CreateAsync();

// Each group is its own class with its own locals and starts from an empty database, so a new
// group can be added without reading every name declared before it.
await SleepChecks.RunAsync(h);
await BottleChecks.RunAsync(h);
await PauseChecks.RunAsync(h);
await EditorChecks.RunAsync(h);
await StockChecks.RunAsync(h);
await DisplayChecks.RunAsync(h);
await DayShapeChecks.RunAsync(h);
await MilestoneChecks.RunAsync(h);

return h.Report();
