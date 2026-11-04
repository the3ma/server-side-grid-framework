import { bootstrapApplication } from '@angular/platform-browser';
import { AllCommunityModule, ModuleRegistry } from 'ag-grid-community';
import { App } from './app/app';
import { appConfig } from './app/app.config';

ModuleRegistry.registerModules([AllCommunityModule]);

// AG Grid follows the page's light / dark scheme.
const dark = window.matchMedia('(prefers-color-scheme: dark)');
const applyScheme = () => (document.body.dataset['agThemeMode'] = dark.matches ? 'dark' : 'light');
applyScheme();
dark.addEventListener('change', applyScheme);

bootstrapApplication(App, appConfig).catch((err) => console.error(err));
