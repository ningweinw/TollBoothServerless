CREATE TABLE VehiclePlate
(
    boothid INT,
    capturetime DATETIME,
    platetext VARCHAR(255),
    imagefile VARCHAR(255)
);

ALTER TABLE VehiclePlate
	ALTER COLUMN platetext ADD MASKED WITH (FUNCTION = 'default()');

CREATE INDEX i_capturetime ON VehiclePlate(capturetime);

GO