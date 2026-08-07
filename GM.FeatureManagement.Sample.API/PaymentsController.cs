using GM.FeatureManagement;
using Microsoft.AspNetCore.Mvc;

namespace GM.FeatureManagement.Sample.API;

/// <summary>
/// Demonstrates the MVC side of gating: the whole action is behind the <c>NewPayments</c> flag via
/// <see cref="FeatureGateAttribute"/>, enforced by the global filter that
/// <c>AddGMFeatureManagementAspNetCore()</c> registers. Returns 404 while the gate is closed.
/// </summary>
[ApiController]
[Route("api/payments")]
public sealed class PaymentsController : ControllerBase
{
    [HttpGet]
    [FeatureGate("NewPayments")]
    public IActionResult Get() => Ok(new { status = "payments API available (NewPayments is on)" });
}
