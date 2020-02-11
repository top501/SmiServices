package org.smi.common.test.options;

import org.smi.common.options.GlobalOptions;

import junit.framework.TestCase;

public class GlobalOptionsTest extends TestCase {

    protected void setUp() throws Exception {

        super.setUp();
        GlobalOptions.Load(true);
    }

    protected void tearDown() throws Exception {

        super.tearDown();
    }

    //TODO Add proper tests here	

    public void testEmpty() {

    }
}
