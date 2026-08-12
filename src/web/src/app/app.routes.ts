import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { ShellComponent } from './layout/shell.component';
import { AuthComponent } from './pages/auth/auth.component';
import { DashboardComponent } from './pages/dashboard/dashboard.component';
import { ServersComponent } from './pages/servers/servers.component';
import { ServerDetailComponent } from './pages/server-detail/server-detail.component';
import { LibraryComponent } from './pages/library/library.component';
import { ActivityComponent } from './pages/activity/activity.component';
import { HostComponent } from './pages/host/host.component';
import { ComingSoonComponent } from './pages/coming-soon/coming-soon.component';
import { PublicServersComponent } from './pages/public-servers/public-servers.component';

export const routes: Routes = [
  { path: 'join', component: PublicServersComponent, title: 'Join a game server' },
  { path: 'auth', component: AuthComponent },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      { path: '', component: DashboardComponent, title: 'Dashboard' },
      { path: 'servers', component: ServersComponent, title: 'Servers' },
      { path: 'servers/:id', component: ServerDetailComponent, title: 'Server' },
      { path: 'library', component: LibraryComponent, title: 'Game Library' },
      { path: 'activity', component: ActivityComponent, title: 'Activity' },
      { path: 'host', component: HostComponent, title: 'Host' },
      { path: 'backups', component: ComingSoonComponent, data: { title: 'Backups', text: 'Backup creation and restore are the next MVP slice.' } },
      { path: 'settings', component: ComingSoonComponent, data: { title: 'Settings', text: 'Host paths and runtimes are currently configured through appsettings or environment variables.' } }
    ]
  },
  { path: '**', redirectTo: '' }
];
