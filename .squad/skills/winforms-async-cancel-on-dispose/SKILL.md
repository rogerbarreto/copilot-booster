# WinForms async cancel on dispose

Use this pattern when a form starts async work that may complete after the form closes.

## Pattern

1. Add a nullable form-owned `CancellationTokenSource` field.
2. Create it when starting the async operation.
3. Pass its token into the service call.
4. On completion, check `this.IsDisposed` and `ct.IsCancellationRequested` before touching UI.
5. Marshal UI changes with `this.BeginInvoke(...)`.
6. In `Dispose(bool disposing)`, cancel, dispose, and null the CTS before calling `base.Dispose(disposing)`.

## Example

```csharp
private CancellationTokenSource? _fetchCts;

private async Task FetchAsync(CancellationToken ct)
{
    try
    {
        var result = await service.GetAsync(ct).ConfigureAwait(true);
        if (this.IsDisposed || ct.IsCancellationRequested)
        {
            return;
        }

        this.BeginInvoke(() => this.ApplyResult(result));
    }
    catch (OperationCanceledException)
    {
    }
}

protected override void Dispose(bool disposing)
{
    if (disposing)
    {
        this._fetchCts?.Cancel();
        this._fetchCts?.Dispose();
        this._fetchCts = null;
    }

    base.Dispose(disposing);
}
```
