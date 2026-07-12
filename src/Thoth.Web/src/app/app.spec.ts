import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient()],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should default to tools-enabled mode', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance.useTools()).toBe(true);
  });

  it('should expose provider and checkpoint quality status chips', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;

    app.systemStatus.set({
      runtimeMode: 'hybrid',
      model: 'thoth-transformer',
      selfContainedOnly: false,
      shellEnabled: false,
      toolCount: 12,
      conversationCount: 2,
      memoryCount: 3,
      time: new Date().toISOString(),
      modelStatus: 'QualifiedForGeneration',
      modelStatusReasons: [],
      activeProvider: 'hybrid',
      checkpointState: 'QualifiedForGeneration',
      qualityQualification: 'generation',
      toolsActive: true,
    });

    const chips = app.statusChips();
    expect(chips.some((chip) => chip.label === 'Checkpoint' && chip.value === 'QualifiedForGeneration')).toBe(true);
    expect(chips.some((chip) => chip.label === 'Quality' && chip.value === 'generation')).toBe(true);
  });
});
