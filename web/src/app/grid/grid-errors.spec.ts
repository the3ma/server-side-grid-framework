import { describe, expect, it } from 'vitest';
import { gridErrorMessage, toGridRequestError } from './grid-errors';

describe('toGridRequestError', () => {
  it('maps grid-request-invalid with the rejected field ids', () => {
    const error = toGridRequestError(400, {
      type: 'grid-request-invalid',
      status: 400,
      errors: { weightKg: ['Value is not a number.'] },
      errorCodes: { weightKg: ['invalidValue'], '(invalid field)': ['unknownField'] },
      traceId: 't-1',
    });
    expect(error.kind).toBe('invalid');
    expect(error.details.fields).toEqual(['weightKg', '(invalid field)']);
    expect(error.details.traceId).toBe('t-1');
  });

  it('maps grid-filter-too-broad with fields and threshold', () => {
    const error = toGridRequestError(400, {
      type: 'grid-filter-too-broad',
      status: 400,
      fields: ['destination', 'search'],
      threshold: 50000,
    });
    expect(error.kind).toBe('tooBroad');
    expect(error.details).toMatchObject({ fields: ['destination', 'search'], threshold: 50000 });
  });

  it('maps timeout, failed, network, forbidden and unknown bodies', () => {
    expect(
      toGridRequestError(504, { type: 'grid-query-timeout', status: 504, timeoutSeconds: 30 }).kind,
    ).toBe('timeout');
    expect(toGridRequestError(500, { type: 'grid-query-failed', status: 500 }).kind).toBe('http');
    expect(toGridRequestError(0, null).kind).toBe('network');
    expect(toGridRequestError(403, '').kind).toBe('forbidden');
    expect(toGridRequestError(400, { message: 'other' }).kind).toBe('badRequest');
    expect(toGridRequestError(502, '<html>').kind).toBe('http');
  });
});

describe('gridErrorMessage', () => {
  it('names columns by header and never contains request values', () => {
    const error = toGridRequestError(400, {
      type: 'grid-filter-too-broad',
      status: 400,
      fields: ['destination', 'search'],
      threshold: 50000,
    });
    const message = gridErrorMessage(error, (f) => (f === 'destination' ? 'Destination' : f));
    expect(message).toContain('Destination, Search');
    expect(message).toContain('50,000');
  });
});
