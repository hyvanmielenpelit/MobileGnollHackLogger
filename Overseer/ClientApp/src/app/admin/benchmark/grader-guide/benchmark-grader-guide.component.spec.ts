import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BenchmarkGraderGuideComponent, GraderGuideProfile, GraderGuideSection } from './benchmark-grader-guide.component';

describe('BenchmarkGraderGuideComponent', () => {
  let fixture: ComponentFixture<BenchmarkGraderGuideComponent>;
  let component: BenchmarkGraderGuideComponent;
  let host: HTMLElement;

  const SECTIONS: GraderGuideSection[] = [
    'roles', 'second-reader', 'coverage', 'reference-reader', 'claim-verifier', 'models', 'recommended'
  ];

  const PROFILE: GraderGuideProfile = {
    name: 'Strict',
    secondOpinionQualityThreshold: 62,
    secondOpinionOutlierDeltaPoints: 18,
    secondOpinionMinimumSample: 7,
    secondOpinionBlind: false
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BenchmarkGraderGuideComponent]
    }).compileComponents();
    fixture = TestBed.createComponent(BenchmarkGraderGuideComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  });

  afterEach(() => component.dialog?.nativeElement?.close());

  const dialog = (): HTMLDialogElement => host.querySelector<HTMLDialogElement>('dialog.benchmark-grader-guide-dialog')!;
  const setting = (name: string): string => host.querySelector(`[data-setting="${name}"]`)!.textContent!.trim();
  const section = (id: GraderGuideSection): HTMLElement => host.querySelector(`#graderGuide-${id}`)!.closest('section')!;

  const setProfile = (profile: GraderGuideProfile | null) => {
    fixture.componentRef.setInput('profile', profile);
    fixture.detectChanges();
  };

  it('shows the dialog modally on open()', () => {
    component.open();
    expect(dialog().open).toBeTrue();
    expect(dialog().matches(':modal')).toBeTrue();
  });

  it('brings the named section heading into view, and carries all seven section ids', () => {
    for (const id of SECTIONS) {
      expect(host.querySelector(`#graderGuide-${id}`)).withContext(id).not.toBeNull();
    }
    const heading = host.querySelector<HTMLElement>('#graderGuide-coverage')!;
    const scroll = spyOn(heading, 'scrollIntoView');
    component.open('coverage');
    expect(dialog().open).toBeTrue();
    expect(scroll).toHaveBeenCalled();
  });

  it('holds both the Use it and the Skip it lists in the Claim Verifier section', () => {
    const verifier = section('claim-verifier');
    expect(verifier.textContent).toContain('Use it when:');
    expect(verifier.textContent).toContain('Skip it when:');
    expect(verifier.querySelectorAll('.grader-guide-use-it li').length).toBeGreaterThan(0);
    expect(verifier.querySelectorAll('.grader-guide-skip-it li').length).toBeGreaterThan(0);
  });

  it('lists one row per grading role and the report writer in the Choosing grader models table', () => {
    const rows = section('models').querySelectorAll('table tbody tr');
    expect(rows.length).toBe(6);
  });

  it('names the Report writer in the grading roles table', () => {
    const roles = Array.from(section('roles').querySelectorAll('table tbody tr td:first-child'))
      .map(cell => cell.textContent!.trim());
    expect(roles).toContain('Report writer');
  });

  it('closes from a close button named Close grader guide', () => {
    component.open();
    const close = host.querySelector<HTMLButtonElement>('button[aria-label="Close grader guide"]');
    expect(close).not.toBeNull();
    close!.click();
    expect(dialog().open).toBeFalse();
  });

  it('prints the profile threshold, delta, sample and blind values', () => {
    setProfile(PROFILE);
    expect(setting('threshold')).toBe('62');
    expect(setting('delta')).toBe('18');
    expect(setting('sample')).toBe('7');
    expect(setting('blind')).toBe('off');
    expect(host.textContent).not.toContain('(Standard profile default)');
    expect(host.textContent).toContain('Strict');
  });

  it('prints the Standard profile defaults, marked as such, without a profile', () => {
    setProfile(null);
    expect(setting('threshold')).toBe('50 (Standard profile default)');
    expect(setting('delta')).toBe('25 (Standard profile default)');
    expect(setting('sample')).toBe('4 (Standard profile default)');
    expect(setting('blind')).toBe('on (Standard profile default)');
  });

  it('prints a threshold of 0 as off', () => {
    setProfile({ ...PROFILE, secondOpinionQualityThreshold: 0 });
    expect(setting('threshold')).toBe('off (critical errors only)');
  });

  it('gives every table column headers', () => {
    const tables = Array.from(host.querySelectorAll('table'));
    expect(tables.length).toBe(4);
    for (const table of tables) {
      const headers = table.querySelectorAll('thead th[scope="col"]');
      expect(headers.length).withContext(table.getAttribute('aria-labelledby') ?? '').toBeGreaterThan(1);
    }
  });
});
