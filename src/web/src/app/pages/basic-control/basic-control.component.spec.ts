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
});
