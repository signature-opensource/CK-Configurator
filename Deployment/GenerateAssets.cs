// Run by CKli after the build of the repository (in this folder, the version as the first argument): builds the
// deployments of this folder (CK.AppDeployment), their published hosts in Assets/{deployment} (one release asset each).
#:package CK.AppDeployment@37.0.1-0.uc.1.ci.35
// The packages that the deployments add to their Apps (Deployment:Apps:{App}:Packages) and the engines they require: a comment for "dotnet" (nothing
// is restored for this script), a package reference of the solution for CKli (upgraded to the World's version).
//ck-package CK.Ng.AppHost.DesktopNgApp@0.0.1-0.uc.0.ci.11
//ck-package CK.AppHost.DesktopNgApp.Engine@0.0.1-0.uc.0.ci.11

return await CK.Setup.AppDeployment.RunAsync( args );
