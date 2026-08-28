import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { BasicControlComponent } from './basic-control.component';

describe('BasicControlComponent', () => {
  it('loads existing servers and selects the first CS2 instance', () => {
    TestBed.configureTestingModule({
      imports: [BasicControlComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(BasicControlComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    http.expectOne('/api/servers').flush([
      { id: 'paper-1', name: 'Paper', templateId: 'minecraft-paper' },
      { id: 'cs2-1', name: 'CS2 Test', templateId: 'counter-strike-2' },
    ]);
    http.expectOne('/api/servers/cs2-1').flush({
      id: 'cs2-1',
      name: 'CS2 Test',
      templateId: 'counter-strike-2',
      process: { isRunning: false },
    });
    http.expectOne('/api/servers/cs2-1/basic-config').flush({
      configuration: { autoBhop: false, gravity: 800, botQuota: 0 },
      running: false,
      appliedLive: false,
      message: 'Gespeichert',
      observedValues: {},
      output: null,
    });

    expect(fixture.componentInstance.selectedServerId()).toBe('cs2-1');
    fixture.destroy();
    http.verify();
  });

  it('registers an existing CS2 host directory without starting an installation', () => {
    TestBed.configureTestingModule({
      imports: [BasicControlComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(BasicControlComponent);
    const component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/servers').flush([]);

    component.importName.set('Existing CS2');
    component.importDirectory.set('D:\\Dedicated\\CS2');
    component.importPort.set(27017);
    component.importExistingCs2();

    const request = http.expectOne('/api/servers/import-cs2');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      name: 'Existing CS2',
      installDirectory: 'D:\\Dedicated\\CS2',
      port: 27017,
      ramLimitMb: 4096,
    });
    request.flush({
      id: 'cs2-imported',
      name: 'Existing CS2',
      templateId: 'counter-strike-2',
      externalInstallation: true,
      process: { isRunning: false },
    });
    http.expectOne('/api/servers/cs2-imported/basic-config').flush({
      configuration: { autoBhop: false, gravity: 800, botQuota: 0 },
      running: false,
      appliedLive: false,
      message: 'Gespeichert',
      observedValues: {},
      output: null,
    });

    expect(component.selectedServerId()).toBe('cs2-imported');
    expect(component.importMessage()).toContain('keine Spieldateien');
    fixture.destroy();
    http.verify();
  });
});
