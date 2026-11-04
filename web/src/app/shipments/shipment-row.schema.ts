import { z } from 'zod';

/** One row of the shipments grid, camelCase as the server sends it. Parsed strictly. */
export const ShipmentRowSchema = z
  .object({
    shipmentId: z.number().int(),
    trackingNo: z.string(),
    warehouseCode: z.string(),
    destination: z.string(),
    status: z.string(),
    carrier: z.string().nullable(),
    weightKg: z.number().nullable(),
    shippedOn: z.string(),
    deliveredOn: z.string().nullable(),
    flagged: z.boolean(),
    priceTier: z.string().nullable(),
  })
  // Exactly the server row: an unexpected key fails the page like a wrong type does.
  .strict();

export type ShipmentRow = z.infer<typeof ShipmentRowSchema>;
