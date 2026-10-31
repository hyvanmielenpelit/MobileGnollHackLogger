import { DragAutoscroller, autoscrollVelocity } from './drag-autoscroll';

describe('autoscrollVelocity', () => {
  it('is 0 in the neutral middle', () => {
    expect(autoscrollVelocity(350, 0, 700, false)).toBe(0);
  });

  it('is the slowest speed at the inner boundary of the zone', () => {
    expect(autoscrollVelocity(540, 0, 700, false)).toBe(40);
    expect(autoscrollVelocity(160, 0, 700, false)).toBe(-40);
  });

  it('follows the worked example for a 700 px scroller', () => {
    expect(autoscrollVelocity(580, 0, 700, false)).toBeCloseTo(121.5, 0);
    expect(autoscrollVelocity(620, 0, 700, false)).toBeCloseTo(366.2, 0);
    expect(autoscrollVelocity(668, 0, 700, false)).toBe(875);
  });

  it('holds top speed past the edge', () => {
    expect(autoscrollVelocity(900, 0, 700, false)).toBe(875);
    expect(autoscrollVelocity(-200, 0, 700, false)).toBe(-875);
  });

  it('is symmetric up and down, and offset by the scroller position', () => {
    expect(autoscrollVelocity(100 + 80, 100, 800, false)).toBeCloseTo(-autoscrollVelocity(800 - 80, 100, 800, false), 6);
  });

  it.each([
    // height, zone, top speed
    [200, 50, 480],
    [300, 75, 480],
    [700, 160, 875],
    [1400, 160, 900]
  ])('clamps the zone and the top speed at a height of %i px', (height, zone, topSpeed) => {
    expect(autoscrollVelocity(height - zone - 1, 0, height, false)).toBe(0);
    expect(autoscrollVelocity(height - zone, 0, height, false)).toBe(40);
    expect(autoscrollVelocity(height, 0, height, false)).toBe(topSpeed);
  });

  it('keeps a neutral middle in a very short scroller', () => {
    expect(autoscrollVelocity(50, 0, 100, false)).toBe(0);
    expect(autoscrollVelocity(66, 0, 100, false)).toBe(0);
    expect(autoscrollVelocity(67, 0, 100, false)).toBeGreaterThan(0);
  });

  it('halves the top speed under reduced motion', () => {
    expect(autoscrollVelocity(700, 0, 700, true)).toBe(437.5);
    expect(autoscrollVelocity(540, 0, 700, true)).toBe(40);
  });

  it('is 0 for a scroller with no visible height', () => {
    expect(autoscrollVelocity(10, 10, 10, false)).toBe(0);
  });
});

describe('DragAutoscroller', () => {
  let scroller: HTMLElement;
  let edges: { top: number; bottom: number };
  let queue: Map<number, FrameRequestCallback>;
  let nextHandle: number;

  /** Runs the queued animation frame callbacks with this timestamp. */
  function frame(timestamp: number): void {
    const callbacks = Array.from(queue.values());
    queue.clear();
    callbacks.forEach(callback => callback(timestamp));
  }

  /** Runs frames every `step` ms, from `from` to `to` inclusive. */
  function frames(from: number, to: number, step: number): void {
    for (let time = from; time <= to + 1e-6; time += step) {
      frame(time);
    }
  }

  const running = () => queue.size > 0;
  const startMiddle = () => (edges.top + edges.bottom) / 2;
  const nearBottom = () => edges.bottom - 1;
  const nearTop = () => edges.top + 1;
  const create = (startY = startMiddle(), onScroll?: () => void) =>
    new DragAutoscroller(scroller, startY, { reducedMotion: false, onScroll });

  beforeEach(() => {
    queue = new Map();
    nextHandle = 1;
    vi.spyOn(window, 'requestAnimationFrame').mockImplementation(callback => {
      const handle = nextHandle++;
      queue.set(handle, callback);
      return handle;
    });
    vi.spyOn(window, 'cancelAnimationFrame').mockImplementation(handle => {
      queue.delete(handle);
    });

    scroller = document.createElement('div');
    scroller.style.cssText = 'position: fixed; top: 0; left: 0; width: 200px; height: 400px; overflow-y: auto;';
    const content = document.createElement('div');
    content.style.height = '20000px';
    scroller.appendChild(content);
    document.body.appendChild(scroller);
    scroller.scrollTop = 10000;
    const rect = scroller.getBoundingClientRect();
    edges = { top: rect.top, bottom: rect.top + scroller.clientHeight };
  });

  afterEach(() => {
    scroller.remove();
    vi.restoreAllMocks();
  });

  it('covers the same distance in one second at 60 Hz and at 120 Hz', () => {
    const distance = (stepMs: number) => {
      scroller.scrollTop = 10000;
      const autoscroller = create();
      autoscroller.update(nearBottom());
      frames(0, 1000, stepMs);
      autoscroller.stop();
      return scroller.scrollTop - 10000;
    };
    const at60 = distance(1000 / 60);
    const at120 = distance(1000 / 120);
    expect(at60).toBeGreaterThan(0);
    expect(Math.abs(at60 - at120)).toBeLessThanOrEqual(4);
  });

  it('reaches full speed at 400 ms', () => {
    const autoscroller = create();
    autoscroller.update(nearBottom());
    frames(0, 400, 10);
    const atFull = scroller.scrollTop;
    frames(410, 500, 10);
    // 500 px/s, the top speed for a 400 px scroller, over 100 ms.
    expect(scroller.scrollTop - atFull).toBeGreaterThanOrEqual(49);
    expect(scroller.scrollTop - atFull).toBeLessThanOrEqual(51);
    autoscroller.stop();
  });

  it('moves less during the ramp-up than at full speed', () => {
    const autoscroller = create();
    autoscroller.update(nearBottom());
    frames(0, 100, 10);
    // The integral of the ramp to 25% of 500 px/s over 100 ms.
    expect(scroller.scrollTop - 10000).toBeLessThanOrEqual(7);
    autoscroller.stop();
  });

  it('restarts the ramp-up after the pointer leaves the zone', () => {
    const autoscroller = create();
    autoscroller.update(nearBottom());
    frames(0, 500, 10);
    autoscroller.update(startMiddle());
    frame(510);
    expect(running()).toBe(false);

    const before = scroller.scrollTop;
    autoscroller.update(nearBottom());
    frames(1000, 1100, 10);
    expect(scroller.scrollTop - before).toBeLessThanOrEqual(7);
    autoscroller.stop();
  });

  it('restarts the ramp-up when the direction flips', () => {
    const autoscroller = create();
    autoscroller.update(nearBottom());
    frames(0, 500, 10);

    const before = scroller.scrollTop;
    autoscroller.update(nearTop());
    frames(510, 610, 10);
    expect(scroller.scrollTop).toBeLessThan(before);
    expect(before - scroller.scrollTop).toBeLessThanOrEqual(7);
    autoscroller.stop();
  });

  it('does not scroll from a stationary pickup inside the zone', () => {
    const autoscroller = create(nearBottom());
    autoscroller.update(nearBottom());
    expect(running()).toBe(false);
    autoscroller.update(nearBottom() - 7);
    expect(running()).toBe(false);
    expect(scroller.scrollTop).toBe(10000);
    autoscroller.stop();
  });

  it('scrolls once the pointer has moved 8 px towards the edge of a zone it was picked up in', () => {
    const start = edges.bottom - 40;
    const autoscroller = create(start);
    autoscroller.update(start + 7);
    expect(running()).toBe(false);
    autoscroller.update(start + 8);
    expect(running()).toBe(true);
    frames(0, 500, 10);
    expect(scroller.scrollTop).toBeGreaterThan(10000);
    autoscroller.stop();
  });

  it('scrolls once the pointer has been outside the zone it was picked up in', () => {
    const autoscroller = create(nearBottom());
    autoscroller.update(startMiddle());
    expect(running()).toBe(false);
    autoscroller.update(nearBottom());
    expect(running()).toBe(true);
    autoscroller.stop();
  });

  it('leaves the opposite direction armed after a pickup inside one zone', () => {
    const autoscroller = create(nearBottom());
    autoscroller.update(nearTop());
    expect(running()).toBe(true);
    frames(0, 500, 10);
    expect(scroller.scrollTop).toBeLessThan(10000);
    autoscroller.stop();
  });

  it('keeps scrolling while the pointer is still', () => {
    const autoscroller = create();
    autoscroller.update(nearBottom());
    frames(0, 200, 10);
    const before = scroller.scrollTop;
    frames(210, 400, 10);
    expect(scroller.scrollTop).toBeGreaterThan(before);
    autoscroller.stop();
  });

  it('accumulates sub-pixel steps at slow speeds', () => {
    const autoscroller = create();
    // The inner boundary of the 100 px zone: 40 px/s, under one pixel per frame at 60 Hz.
    autoscroller.update(edges.bottom - 100);
    frames(0, 400, 1000 / 60);
    const before = scroller.scrollTop;
    frames(400 + 1000 / 60, 1400, 1000 / 60);
    expect(scroller.scrollTop - before).toBeGreaterThanOrEqual(38);
    expect(scroller.scrollTop - before).toBeLessThanOrEqual(42);
    autoscroller.stop();
  });

  it('calls onScroll only on frames that moved the scroller', () => {
    const onScroll = vi.fn();
    const autoscroller = create(startMiddle(), onScroll);
    autoscroller.update(nearBottom());
    frame(0);
    frame(1);
    expect(onScroll).not.toHaveBeenCalled();
    frames(11, 400, 10);
    expect(onScroll).toHaveBeenCalled();
    autoscroller.stop();
  });

  it('ends the loop at the end of the range, and update() restarts it', () => {
    scroller.scrollTop = scroller.scrollHeight;
    const autoscroller = create();
    autoscroller.update(nearBottom());
    frames(0, 200, 10);
    expect(running()).toBe(false);

    autoscroller.update(nearBottom());
    expect(running()).toBe(true);
    autoscroller.update(nearTop());
    frames(300, 800, 10);
    expect(scroller.scrollTop).toBeLessThan(scroller.scrollHeight - scroller.clientHeight);
    autoscroller.stop();
  });

  it('does not read a frame that asks for less than a pixel as the end of the range', () => {
    scroller.scrollTop = scroller.scrollHeight;
    const autoscroller = create();
    autoscroller.update(edges.bottom - 100);
    frame(0);
    frame(1);
    frame(2);
    expect(running()).toBe(true);
    autoscroller.stop();
  });

  it('counts a 1000 ms frame gap as no more than a 50 ms frame', () => {
    const autoscroller = create();
    autoscroller.update(nearBottom());
    frames(0, 500, 10);
    const beforeNormal = scroller.scrollTop;
    frame(550);
    const normal = scroller.scrollTop - beforeNormal;
    const beforeGap = scroller.scrollTop;
    frame(1550);
    expect(scroller.scrollTop - beforeGap).toBeLessThanOrEqual(normal + 1);
    autoscroller.stop();
  });

  it('pause() ends the loop until the next update()', () => {
    const autoscroller = create();
    autoscroller.update(nearBottom());
    frames(0, 100, 10);
    autoscroller.pause();
    expect(running()).toBe(false);

    const before = scroller.scrollTop;
    frame(200);
    expect(scroller.scrollTop).toBe(before);

    autoscroller.update(nearBottom());
    expect(running()).toBe(true);
    autoscroller.stop();
  });

  it('stop() ends the loop for good, and a second stop() is harmless', () => {
    const autoscroller = create();
    autoscroller.update(nearBottom());
    autoscroller.stop();
    expect(running()).toBe(false);
    expect(() => autoscroller.stop()).not.toThrow();
    autoscroller.update(nearBottom());
    expect(running()).toBe(false);
  });
});
