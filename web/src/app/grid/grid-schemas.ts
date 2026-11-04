import { z } from 'zod';
import type { GridPage, GridValuesPage } from './grid-request.types';

const CapabilitiesSchema = z.object({
  textFilters: z.enum(['enabled', 'disabled']),
  reason: z.literal('resultSetTooLarge').optional(),
  threshold: z.number().int().nonnegative().optional(),
});

/**
 * Strict page parser: one row that does not match the row schema fails the whole page.
 * A partial grid would look like a complete one, so it is an error instead.
 */
export function GridPageSchema<TRow>(rowSchema: z.ZodType<TRow, z.ZodTypeDef, unknown>) {
  return z.object({
    rows: z.array(rowSchema),
    totalCount: z.number().int().nonnegative().nullable(),
    capabilities: CapabilitiesSchema,
  }) as unknown as z.ZodType<GridPage<TRow>, z.ZodTypeDef, unknown>;
}

export const GridValuesPageSchema: z.ZodType<GridValuesPage, z.ZodTypeDef, unknown> = z.object({
  values: z.array(z.string()).max(1000),
  truncated: z.boolean(),
});

const ProblemBase = z.object({
  status: z.number().int(),
  title: z.string().optional(),
  traceId: z.string().nullish(),
});

export const GridInvalidProblemSchema = ProblemBase.extend({
  type: z.literal('grid-request-invalid'),
  errors: z.record(z.array(z.string())).default({}),
  errorCodes: z.record(z.array(z.string())).default({}),
});

export const GridTooBroadProblemSchema = ProblemBase.extend({
  type: z.literal('grid-filter-too-broad'),
  fields: z.array(z.string()),
  threshold: z.number().int().nonnegative(),
});

export const GridTimeoutProblemSchema = ProblemBase.extend({
  type: z.literal('grid-query-timeout'),
  timeoutSeconds: z.number().nonnegative(),
});

export const GridFailedProblemSchema = ProblemBase.extend({
  type: z.literal('grid-query-failed'),
});

export const GridProblemSchema = z.discriminatedUnion('type', [
  GridInvalidProblemSchema,
  GridTooBroadProblemSchema,
  GridTimeoutProblemSchema,
  GridFailedProblemSchema,
]);

export type GridProblem = z.infer<typeof GridProblemSchema>;
