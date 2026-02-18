// This file intentionally contains no members.
//
// We previously experimented with additional string-based overloads for endpoint compatibility.
// Those overloads were consolidated into JsonFileControlPlane.EndpointCompat.cs / *.RevokeCompat.cs.
//
// Keeping this file (empty) prevents patch application issues on systems that can't represent file deletions
// while ensuring we do not introduce duplicate method signatures.

namespace Safe0ne.DashboardServer.ControlPlane;
