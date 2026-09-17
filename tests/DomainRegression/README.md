# Domain regression suite

Run `dotnet run --project tests/DomainRegression/BDVM.Domain.Tests.csproj -c Release`
from BDVM.Full. Keep the BDVM module repositories side by side under the same
parent directory. This publishes the aggregate regression suite used for the
September 17 candidate, including wallet recovery and pricing checks.
