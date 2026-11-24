import { Routes } from '@angular/router';
import CKGenRoutes from '@local/ck-gen/CK/Angular/routes';

export const routes: Routes = [
 // The configurator opens on its catalog (the generated routes end with a fallback to "/").
 { path: '', pathMatch: 'full', redirectTo: 'ckomposable/catalog' },
 ...CKGenRoutes
];
